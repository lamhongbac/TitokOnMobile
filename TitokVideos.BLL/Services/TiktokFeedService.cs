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
    /// <summary>
    /// Service đồng bộ TikTok Feed.
    /// <para>
    /// <see cref="SyncTiktokFeedsAsync"/> là hàm DUY NHẤT thực hiện đồng bộ, được dùng chung bởi:
    /// nút Manual Sync (GUI - môi trường thử nghiệm) và Auto Sync (Worker - triển khai thực tế).
    /// </para>
    /// <para>
    /// Các thao tác hạ tầng (gọi RapidAPI, tải thumbnail, tải MP4) mỗi thứ chỉ có 1 hàm private,
    /// được tái sử dụng cho cả Sync và Migrate.
    /// </para>
    /// </summary>
    public class TiktokFeedService : ITiktokFeedService
    {
        /// <summary>Tiền tố URL local của thumbnail: Sync/Migrate ghi vào DB, GetFeedsAsync đọc ra. Dùng chung 1 nguồn.</summary>
        private const string ThumbnailUrlPrefix = "/thumbnails/";

        /// <summary>Tiền tố URL local của video MP4: Sync/Migrate ghi vào DB, GetFeedsAsync đọc ra. Dùng chung 1 nguồn.</summary>
        private const string VideoUrlPrefix = "/videos/";

        private readonly ITiktokFeedRepository _repository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TiktokFeedService> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>Dữ liệu video lấy về từ RapidAPI (đã parse).</summary>
        private sealed record RemoteVideo(
            string VideoId,
            string Desc,
            string CoverUrl,
            string PlayAddr,
            DateTimeOffset PublishedAt,
            int PlayCount);

        /// <summary>Kết quả tải media về local. Mỗi URL là null nếu không có nguồn hoặc tải lỗi.</summary>
        private sealed record LocalMedia(string? ThumbnailUrl, string? MediaUrl);

        public TiktokFeedService(ITiktokFeedRepository repository, IHttpClientFactory httpClientFactory, ILogger<TiktokFeedService> logger, IConfiguration configuration)
        {
            _repository = repository;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        #region Public API

        /// <summary>
        /// Smart Sync: lấy video mới từ TikTok (mới hơn mốc PublishedAt trong DB), tải media về local và lưu DB.
        /// Dùng chung cho GUI (Manual Sync) và Worker (Auto Sync).
        /// </summary>
        public async Task<BOProcessResult> SyncTiktokFeedsAsync()
        {
            try
            {
                // 1. Lấy mốc thời gian mới nhất trong CSDL
                var latestPublishedAt = await _repository.GetLatestPublishedAtAsync();
                var cutoffTime = latestPublishedAt ?? DateTimeOffset.MinValue;
                _logger.LogInformation("[Sync] Bước 1: Mốc PublishedAt mới nhất trong DB = {Cutoff}",
                    latestPublishedAt.HasValue ? latestPublishedAt.Value.ToString("dd/MM/yyyy HH:mm:ss zzz") : "(chưa có dữ liệu)");

                // 2. Gọi API RapidAPI
                using var client = _httpClientFactory.CreateClient();
                List<RemoteVideo> remoteVideos;
                try
                {
                    remoteVideos = await FetchRemoteVideosAsync(client);
                }
                catch (InvalidDataException ex)
                {
                    return new BOProcessResult { OK = false, Message = ex.Message };
                }

                int syncCount = 0;
                int scanned = 0, duplicateCount = 0;
                int thumbOk = 0, thumbFail = 0, videoOk = 0, videoFail = 0;
                var newVideoIds = new List<string>();
                var mediaErrorIds = new List<string>();
                _logger.LogInformation("[Sync] Bước 3: API trả về {Total} video. Bắt đầu Smart Sync...", remoteVideos.Count);

                // 3. Xử lý "Smart Sync"
                foreach (var video in remoteVideos)
                {
                    scanned++;

                    if (video.PublishedAt <= cutoffTime)
                    {
                        // Đã chạm tới video cũ, ngưng quét
                        _logger.LogInformation("[Sync] Gặp video cũ (PublishedAt={PublishedAt:dd/MM/yyyy HH:mm}) tại vị trí {Index}, dừng quét.", video.PublishedAt, scanned);
                        break;
                    }

                    // Check duplicate again just in case
                    var existing = await _repository.GetByVideoIdAsync(video.VideoId);
                    if (existing != null)
                    {
                        duplicateCount++;
                        _logger.LogDebug("[Sync] Bỏ qua video {VideoId} (đã tồn tại).", video.VideoId);
                        continue;
                    }

                    bool mediaError = false;

                    // Hàm nhân dùng chung: tải thumbnail + MP4 về local. Lỗi thì fallback về URL gốc.
                    var media = await DownloadMediaAsync(client, video.VideoId, video.CoverUrl, video.PlayAddr);

                    if (!string.IsNullOrEmpty(video.CoverUrl))
                    {
                        if (media.ThumbnailUrl != null) thumbOk++; else { thumbFail++; mediaError = true; }
                    }
                    if (!string.IsNullOrEmpty(video.PlayAddr))
                    {
                        if (media.MediaUrl != null) videoOk++; else { videoFail++; mediaError = true; }
                    }

                    string localThumbnailUrl = media.ThumbnailUrl ?? video.CoverUrl;
                    string localMediaUrl = media.MediaUrl ?? video.PlayAddr;

                    var entity = new TiktokFeed
                    {
                        VideoId = video.VideoId,
                        Title = video.Desc,
                        ThumbnailUrl = localThumbnailUrl,
                        MediaUrl = localMediaUrl,
                        PublishedAt = video.PublishedAt,
                        ViewCount = video.PlayCount
                    };

                    await _repository.AddAsync(entity);
                    syncCount++;
                    newVideoIds.Add(video.VideoId);
                    if (mediaError) mediaErrorIds.Add(video.VideoId);
                    _logger.LogInformation("[Sync] + Đã thêm video {VideoId} | PublishedAt={PublishedAt:dd/MM/yyyy HH:mm} | Views={Views} | Thumb={Thumb} | Media={Media} | Title=\"{Title}\"",
                        video.VideoId, video.PublishedAt, video.PlayCount, localThumbnailUrl, localMediaUrl,
                        video.Desc.Length > 80 ? video.Desc.Substring(0, 80) + "..." : video.Desc);
                }

                var summary = $"Đồng bộ thành công {syncCount} video mới. " +
                              $"(API trả về: {remoteVideos.Count}, đã quét: {scanned}, trùng: {duplicateCount}, " +
                              $"thumbnail OK/Lỗi: {thumbOk}/{thumbFail}, MP4 OK/Lỗi: {videoOk}/{videoFail})";
                if (newVideoIds.Any()) summary += $" | Video mới: {string.Join(", ", newVideoIds)}";
                if (mediaErrorIds.Any()) summary += $" | Video lỗi tải media: {string.Join(", ", mediaErrorIds)}";

                return new BOProcessResult
                {
                    OK = true,
                    Result = syncCount,
                    Message = summary
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
                    ThumbnailUrl = ToPublicUrl(baseUrl, e.ThumbnailUrl, ThumbnailUrlPrefix),
                    MediaUrl = ToPublicUrl(baseUrl, e.MediaUrl, VideoUrlPrefix),
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

        /// <summary>
        /// Tải lại thumbnail/MP4 về local cho các video cũ còn đang dùng URL TikTok.
        /// Dùng chung hàm gọi RapidAPI và hàm download với <see cref="SyncTiktokFeedsAsync"/>.
        /// </summary>
        public async Task<BOProcessResult> MigrateOldThumbnailsAsync(int pageIndex, int pageSize)
        {
            try
            {
                var oldFeeds = (await _repository.GetOldThumbnailsPagedAsync(pageIndex, pageSize)).ToList();
                if (!oldFeeds.Any())
                    return new BOProcessResult { OK = true, Message = "Không có video cũ nào cần migrate ở trang này." };

                int successCount = 0;
                int failCount = 0;

                using var client = _httpClientFactory.CreateClient();

                // 1. Gọi lại RapidAPI để lấy URL mới (do URL cũ của TikTok bị hết hạn Token x-expires)
                var freshVideos = new Dictionary<string, RemoteVideo>();
                try
                {
                    foreach (var v in await FetchRemoteVideosAsync(client))
                    {
                        if (!string.IsNullOrEmpty(v.VideoId)) freshVideos[v.VideoId] = v;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể lấy URL mới từ RapidAPI.");
                }

                // 2. Tải ảnh/video với URL mới
                var failedVideoIds = new List<string>();

                foreach (var feed in oldFeeds)
                {
                    freshVideos.TryGetValue(feed.VideoId, out var fresh);
                    string targetUrl = !string.IsNullOrEmpty(fresh?.CoverUrl) ? fresh!.CoverUrl : feed.ThumbnailUrl;
                    string targetVideoUrl = !string.IsNullOrEmpty(fresh?.PlayAddr) ? fresh!.PlayAddr : feed.MediaUrl;

                    var media = await DownloadMediaAsync(client, feed.VideoId, targetUrl, targetVideoUrl);

                    if (media.ThumbnailUrl != null)
                        await _repository.UpdateThumbnailUrlAsync(feed.Id, media.ThumbnailUrl);
                    if (media.MediaUrl != null)
                        await _repository.UpdateMediaUrlAsync(feed.Id, media.MediaUrl);

                    if (media.ThumbnailUrl == null)
                    {
                        failCount++;
                        failedVideoIds.Add(feed.VideoId);
                        continue;
                    }

                    successCount++;
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

        #endregion

        #region Shared helpers (single source of truth)

        /// <summary>
        /// Gọi RapidAPI lấy danh sách video của tài khoản và parse sang <see cref="RemoteVideo"/>.
        /// </summary>
        /// <exception cref="HttpRequestException">API trả về HTTP lỗi.</exception>
        /// <exception cref="InvalidDataException">Response không đúng định dạng (không có itemList).</exception>
        private async Task<List<RemoteVideo>> FetchRemoteVideosAsync(HttpClient client)
        {
            var host = _configuration["RapidApi:Host"];
            var key = _configuration["RapidApi:Key"];
            var secUid = _configuration["RapidApi:SecUid"];

            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://{host}/api/user/posts?secUid={secUid}"),
                Headers =
                {
                    { "x-rapidapi-host", host },
                    { "x-rapidapi-key", key },
                }
            };

            _logger.LogInformation("[RapidAPI] Gọi {Host} lấy danh sách video...", host);
            using var response = await client.SendAsync(request);
            _logger.LogInformation("[RapidAPI] Trả về HTTP {StatusCode}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var dataObj) ||
                !dataObj.TryGetProperty("itemList", out var itemList))
            {
                // Fallback to older format if "data" wrapper doesn't exist
                if (!doc.RootElement.TryGetProperty("itemList", out itemList))
                {
                    _logger.LogWarning("[RapidAPI] Response không hợp lệ, không tìm thấy itemList. Body (500 ký tự đầu): {Body}",
                        body.Length > 500 ? body.Substring(0, 500) : body);
                    throw new InvalidDataException("API Response format is invalid. Cannot find itemList.");
                }
            }

            var result = new List<RemoteVideo>();
            if (itemList.ValueKind != JsonValueKind.Array) return result;

            foreach (JsonElement video in itemList.EnumerateArray())
            {
                var videoObj = video.GetProperty("video");
                int playCount = video.TryGetProperty("stats", out var stats) && stats.TryGetProperty("playCount", out var pc) ? pc.GetInt32() : 0;

                result.Add(new RemoteVideo(
                    VideoId: video.GetProperty("id").GetString() ?? string.Empty,
                    Desc: video.TryGetProperty("desc", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                    CoverUrl: videoObj.TryGetProperty("cover", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                    PlayAddr: videoObj.TryGetProperty("playAddr", out var p) ? p.GetString() ?? string.Empty : string.Empty,
                    PublishedAt: DateTimeOffset.FromUnixTimeSeconds(video.GetProperty("createTime").GetInt64()),
                    PlayCount: playCount));
            }

            return result;
        }

        /// <summary>
        /// HÀM NHÂN: tải thumbnail + MP4 về local và trả về URL local tương đối (/thumbnails/..., /videos/...).
        /// Dùng chung cho <see cref="SyncTiktokFeedsAsync"/> và <see cref="MigrateOldThumbnailsAsync"/>;
        /// mỗi bên tự quyết định cách lưu DB (insert / update) và fallback khi lỗi.
        /// </summary>
        private async Task<LocalMedia> DownloadMediaAsync(HttpClient client, string videoId, string? coverUrl, string? playAddr)
        {
            string? thumb = string.IsNullOrEmpty(coverUrl) ? null : await DownloadThumbnailAsync(client, videoId, coverUrl);
            string? media = string.IsNullOrEmpty(playAddr) ? null : await DownloadVideoAsync(client, videoId, playAddr);
            return new LocalMedia(thumb, media);
        }

        /// <summary>Tải thumbnail về local. Trả về URL tương đối (/thumbnails/xxx.jpg) hoặc null nếu lỗi.</summary>
        private async Task<string?> DownloadThumbnailAsync(HttpClient client, string videoId, string sourceUrl)
        {
            try
            {
                string storagePath = EnsureStoragePath("ApiSettings:ThumbnailStoragePath", "thumbnails");
                string fileName = $"{videoId}.jpg";

                var imageBytes = await client.GetByteArrayAsync(sourceUrl);
                await File.WriteAllBytesAsync(Path.Combine(storagePath, fileName), imageBytes);

                return $"{ThumbnailUrlPrefix}{fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tải thumbnail cho video {VideoId}", videoId);
                return null;
            }
        }

        /// <summary>Tải video MP4 về local (stream). Trả về URL tương đối (/videos/xxx.mp4) hoặc null nếu lỗi.</summary>
        private async Task<string?> DownloadVideoAsync(HttpClient client, string videoId, string sourceUrl)
        {
            try
            {
                string storagePath = EnsureStoragePath("ApiSettings:VideoStoragePath", "videos");
                string fileName = $"{videoId}.mp4";

                using var videoResponse = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead);
                videoResponse.EnsureSuccessStatusCode();
                using var fileStream = new FileStream(Path.Combine(storagePath, fileName), FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                await videoResponse.Content.CopyToAsync(fileStream);

                return $"{VideoUrlPrefix}{fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tải video MP4 cho {VideoId}", videoId);
                return null;
            }
        }

        /// <summary>Lấy đường dẫn lưu trữ từ cấu hình (mặc định wwwroot/{folder}) và tạo thư mục nếu chưa có.</summary>
        private string EnsureStoragePath(string configKey, string defaultFolder)
        {
            string path = _configuration[configKey] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", defaultFolder);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Chuyển URL local (/thumbnails/xxx.jpg, /videos/xxx.mp4 - do Sync/Migrate lưu) thành URL đầy đủ
        /// bằng ApiSettings:BaseUrl. URL TikTok gốc (http...) được giữ nguyên.
        /// </summary>
        private static string ToPublicUrl(string? baseUrl, string? url, string localPrefix)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            if (!string.IsNullOrEmpty(baseUrl) && url.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
                return $"{baseUrl.TrimEnd('/')}{url}";
            return url;
        }

        #endregion
    }
}
