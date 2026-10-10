using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TitokVideos.BLL.BOs;
using TitokVideos.DAL.Entities;
using TitokVideos.DAL.Repositories;
using TitokVideos.Shared.Models;

namespace TitokVideos.BLL.Services
{
    public class TiktokFeedService : ITiktokFeedService
    {
        private readonly ITiktokFeedRepository _repository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TiktokFeedService> _logger;
        private readonly IConfiguration _configuration;

        public TiktokFeedService(ITiktokFeedRepository repository, IHttpClientFactory httpClientFactory, ILogger<TiktokFeedService> logger, IConfiguration configuration)
        {
            _repository = repository;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<BOProcessResult> SyncTiktokFeedsAsync()
        {
            try
            {
                // 1. Lấy mốc thời gian mới nhất trong CSDL
                var latestPublishedAt = await _repository.GetLatestPublishedAtAsync();
                var cutoffTime = latestPublishedAt ?? DateTimeOffset.MinValue;

                // 2. Gọi API RapidAPI
                var host = _configuration["RapidApi:Host"];
                var key = _configuration["RapidApi:Key"];
                var secUid = _configuration["RapidApi:SecUid"];

                using var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"https://{host}/api/user/posts?secUid={secUid}"),
                    Headers =
                    {
                        { "x-rapidapi-host", host },
                        { "x-rapidapi-key", key },
                    }
                };

                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                
                if (!doc.RootElement.TryGetProperty("data", out var dataObj) || 
                    !dataObj.TryGetProperty("itemList", out var itemList))
                {
                    // Fallback to older format if "data" wrapper doesn't exist
                    if (!doc.RootElement.TryGetProperty("itemList", out itemList))
                    {
                        return new BOProcessResult { OK = false, Message = "API Response format is invalid. Cannot find itemList." };
                    }
                }

                int syncCount = 0;

                // 3. Xử lý "Smart Sync"
                foreach (JsonElement video in itemList.EnumerateArray())
                {
                    long createTimeUnix = video.GetProperty("createTime").GetInt64();
                    DateTimeOffset publishedAt = DateTimeOffset.FromUnixTimeSeconds(createTimeUnix);

                    if (publishedAt <= cutoffTime)
                    {
                        // Đã chạm tới video cũ, ngưng quét
                        break;
                    }

                    string videoId = video.GetProperty("id").GetString() ?? string.Empty;
                    
                    // Check duplicate again just in case
                    var existing = await _repository.GetByVideoIdAsync(videoId);
                    if (existing != null) continue;

                    string desc = video.GetProperty("desc").GetString() ?? string.Empty;
                    string coverUrl = video.GetProperty("video").GetProperty("cover").GetString() ?? string.Empty;
                    string playAddr = video.GetProperty("video").GetProperty("playAddr").GetString() ?? string.Empty;
                    int statPlayCount = video.TryGetProperty("stats", out var stats) ? (stats.TryGetProperty("playCount", out var pc) ? pc.GetInt32() : 0) : 0;

                    string localThumbnailUrl = coverUrl;
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        try
                        {
                            string storagePath = _configuration["ApiSettings:ThumbnailStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "thumbnails");
                            if (!Directory.Exists(storagePath))
                            {
                                Directory.CreateDirectory(storagePath);
                            }

                            string fileName = $"{videoId}.jpg";
                            string filePath = Path.Combine(storagePath, fileName);

                            var imageBytes = await client.GetByteArrayAsync(coverUrl);
                            await File.WriteAllBytesAsync(filePath, imageBytes);

                            localThumbnailUrl = $"/thumbnails/{fileName}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Lỗi tải thumbnail cho video {videoId}");
                        }
                    }

                    string localMediaUrl = playAddr;
                    if (!string.IsNullOrEmpty(playAddr))
                    {
                        try
                        {
                            string videoStoragePath = _configuration["ApiSettings:VideoStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "videos");
                            if (!Directory.Exists(videoStoragePath)) Directory.CreateDirectory(videoStoragePath);

                            string videoFileName = $"{videoId}.mp4";
                            string videoFilePath = Path.Combine(videoStoragePath, videoFileName);

                            using var videoResponse = await client.GetAsync(playAddr, HttpCompletionOption.ResponseHeadersRead);
                            videoResponse.EnsureSuccessStatusCode();
                            using var fileStream = new FileStream(videoFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                            await videoResponse.Content.CopyToAsync(fileStream);

                            localMediaUrl = $"/videos/{videoFileName}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Lỗi tải video MP4 cho {videoId}");
                        }
                    }

                    var entity = new TiktokFeed
                    {
                        VideoId = videoId,
                        Title = desc,
                        ThumbnailUrl = localThumbnailUrl,
                        MediaUrl = localMediaUrl,
                        PublishedAt = publishedAt,
                        ViewCount = statPlayCount
                    };

                    await _repository.AddAsync(entity);
                    syncCount++;
                }

                return new BOProcessResult 
                { 
                    OK = true, 
                    Result = syncCount, 
                    Message = $"Đồng bộ thành công {syncCount} video mới." 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Exception trong quá trình đồng bộ dữ liệu TikTok.");
                return new BOProcessResult { OK = false, Message = $"Lỗi khi đồng bộ: {ex.Message}" };
            }
        }

        public async Task<BOProcessResult> GetFeedsAsync(int pageIndex, int pageSize)
        {
            try
            {
                var rawEntities = (await _repository.GetPagedAsync(pageIndex, pageSize + 1)).ToList();
                bool hasNextPage = rawEntities.Count > pageSize;
                var entities = hasNextPage ? rawEntities.Take(pageSize) : rawEntities;

                var baseUrl = _configuration["ApiSettings:BaseUrl"];

                var bos = entities.Select(e => new TiktokFeedBO
                {
                    Id = e.Id,
                    VideoId = e.VideoId,
                    Title = e.Title,
                    ThumbnailUrl = !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(e.ThumbnailUrl) && e.ThumbnailUrl.StartsWith("/thumbnails/") 
                                    ? $"{baseUrl.TrimEnd('/')}{e.ThumbnailUrl}" 
                                    : e.ThumbnailUrl,
                    MediaUrl = !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(e.MediaUrl) && e.MediaUrl.StartsWith("/videos/") 
                                    ? $"{baseUrl.TrimEnd('/')}{e.MediaUrl}" 
                                    : e.MediaUrl,
                    PublishedAt = e.PublishedAt,
                    ViewCount = e.ViewCount,
                    IsActive = e.IsActive
                }).ToList();

                return new BOProcessResult { OK = true, Result = bos, Message = "Success", HasNextPage = hasNextPage };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Exception khi truy vấn danh sách Feeds.");
                return new BOProcessResult { OK = false, Message = $"Lỗi truy vấn: {ex.Message}" };
            }
        }
        public async Task<BOProcessResult> MigrateOldThumbnailsAsync()
        {
            try
            {
                var oldFeeds = (await _repository.GetOldThumbnailsAsync()).ToList();
                if (!oldFeeds.Any())
                    return new BOProcessResult { OK = true, Message = "Không có video cũ nào cần migrate." };

                int successCount = 0;
                int failCount = 0;

                string storagePath = _configuration["ApiSettings:ThumbnailStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "thumbnails");
                if (!Directory.Exists(storagePath))
                {
                    Directory.CreateDirectory(storagePath);
                }

                string videoStoragePath = _configuration["ApiSettings:VideoStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "videos");
                if (!Directory.Exists(videoStoragePath)) Directory.CreateDirectory(videoStoragePath);

                using var client = _httpClientFactory.CreateClient();

                // 1. Gọi lại RapidAPI để lấy URL mới (do URL cũ của TikTok bị hết hạn Token x-expires)
                var host = _configuration["RapidApi:Host"];
                var key = _configuration["RapidApi:Key"];
                var secUid = _configuration["RapidApi:SecUid"];

                var freshUrls = new Dictionary<string, string>();
                var freshVideoUrls = new Dictionary<string, string>();
                try
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri($"https://{host}/api/user/posts?secUid={secUid}"),
                        Headers = { { "x-rapidapi-host", host }, { "x-rapidapi-key", key } }
                    };

                    using var apiResponse = await client.SendAsync(request);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        var body = await apiResponse.Content.ReadAsStringAsync();
                        using JsonDocument doc = JsonDocument.Parse(body);
                        
                        JsonElement itemList = default;
                        bool hasData = doc.RootElement.TryGetProperty("data", out var dataObj) && dataObj.TryGetProperty("itemList", out itemList);
                        if (!hasData) doc.RootElement.TryGetProperty("itemList", out itemList);

                        if (itemList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement video in itemList.EnumerateArray())
                            {
                                string vid = video.GetProperty("id").GetString() ?? "";
                                string cover = video.GetProperty("video").GetProperty("cover").GetString() ?? "";
                                string playAddr = video.GetProperty("video").GetProperty("playAddr").GetString() ?? "";
                                if (!string.IsNullOrEmpty(vid))
                                {
                                    if (!string.IsNullOrEmpty(cover)) freshUrls[vid] = cover;
                                    if (!string.IsNullOrEmpty(playAddr)) freshVideoUrls[vid] = playAddr;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể lấy URL mới từ RapidAPI.");
                }

                // 2. Tải ảnh với URL mới
                var failedVideoIds = new List<string>();

                foreach (var feed in oldFeeds)
                {
                    try
                    {
                        string targetUrl = freshUrls.ContainsKey(feed.VideoId) ? freshUrls[feed.VideoId] : feed.ThumbnailUrl;
                        string targetVideoUrl = freshVideoUrls.ContainsKey(feed.VideoId) ? freshVideoUrls[feed.VideoId] : feed.MediaUrl;

                        string fileName = $"{feed.VideoId}.jpg";
                        string filePath = Path.Combine(storagePath, fileName);

                        var imageBytes = await client.GetByteArrayAsync(targetUrl);
                        await File.WriteAllBytesAsync(filePath, imageBytes);

                        string localThumbnailUrl = $"/thumbnails/{fileName}";
                        await _repository.UpdateThumbnailUrlAsync(feed.Id, localThumbnailUrl);

                        if (!string.IsNullOrEmpty(targetVideoUrl) && targetVideoUrl.StartsWith("http"))
                        {
                            string videoFileName = $"{feed.VideoId}.mp4";
                            string videoFilePath = Path.Combine(videoStoragePath, videoFileName);
                            using var videoResponse = await client.GetAsync(targetVideoUrl, HttpCompletionOption.ResponseHeadersRead);
                            if (videoResponse.IsSuccessStatusCode)
                            {
                                using var fileStream = new FileStream(videoFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                                await videoResponse.Content.CopyToAsync(fileStream);
                                string localMediaUrl = $"/videos/{videoFileName}";
                                await _repository.UpdateMediaUrlAsync(feed.Id, localMediaUrl);
                            }
                        }

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Lỗi migrate thumbnail cho video {feed.VideoId}: {ex.Message}");
                        failCount++;
                        failedVideoIds.Add(feed.VideoId);
                    }
                }

                string responseMsg = $"Migrate thành công {successCount} video. Lỗi {failCount} video.";
                if (failedVideoIds.Any())
                {
                    responseMsg += $" Các Video ID lỗi: {string.Join(", ", failedVideoIds)}";
                }

                return new BOProcessResult 
                { 
                    OK = true, 
                    Message = responseMsg,
                    Result = failedVideoIds
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Exception khi migrate thumbnail.");
                return new BOProcessResult { OK = false, Message = $"Lỗi truy vấn: {ex.Message}" };
            }
        }
    }
}
