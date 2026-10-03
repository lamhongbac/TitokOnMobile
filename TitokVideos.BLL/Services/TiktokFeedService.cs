using System;
using System.Collections.Generic;
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

                    var entity = new TiktokFeed
                    {
                        VideoId = videoId,
                        Title = desc,
                        ThumbnailUrl = coverUrl,
                        MediaUrl = playAddr,
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

                var bos = entities.Select(e => new TiktokFeedBO
                {
                    Id = e.Id,
                    VideoId = e.VideoId,
                    Title = e.Title,
                    ThumbnailUrl = e.ThumbnailUrl,
                    MediaUrl = e.MediaUrl,
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
    }
}
