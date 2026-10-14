using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TitokVideos.Shared.Models;

namespace TitokVideos.Worker
{
    public class TiktokSyncWorker : BackgroundService
    {
        private readonly ILogger<TiktokSyncWorker> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly string _apiBaseUrl;

        public TiktokSyncWorker(ILogger<TiktokSyncWorker> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _apiBaseUrl = _configuration["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background Sync Worker is starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Lấy giờ chạy từ cấu hình, mặc định 2:00 sáng
                var runHour = _configuration.GetValue<int>("SyncJob:RunAtHour", 2);
                var runMinute = _configuration.GetValue<int>("SyncJob:RunAtMinute", 0);

                var now = DateTime.Now;
                var nextRun = new DateTime(now.Year, now.Month, now.Day, runHour, runMinute, 0);

                // Nếu thời gian đã qua trong ngày hôm nay, đặt lịch cho ngày mai
                if (now >= nextRun)
                {
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;
                _logger.LogInformation($"Next sync job scheduled at: {nextRun:dd/MM/yyyy HH:mm:ss} (in {delay.TotalHours:F2} hours).");

                // Sleep process cho đến thời điểm đã định
                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    var runId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    var startedAt = DateTime.Now;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string status = "UNKNOWN";
                    string message = string.Empty;

                    using (_logger.BeginScope("RunId:{RunId}", runId))
                    {
                        _logger.LogInformation("========== [RUN {RunId}] BẮT ĐẦU Tiktok Feed Smart Sync lúc {StartedAt:dd/MM/yyyy HH:mm:ss} ==========", runId, startedAt);

                        try
                        {
                            var client = _httpClientFactory.CreateClient();
                            var response = await client.PostAsync($"{_apiBaseUrl}/api/v1/tiktok/videos/sync", null, stoppingToken);
                            response.EnsureSuccessStatusCode();

                            var json = await response.Content.ReadAsStringAsync(stoppingToken);
                            var result = JsonSerializer.Deserialize<BOProcessResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            status = result?.OK == true ? "SUCCESS" : "FAILED";
                            message = result?.Message ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            status = "CRASHED";
                            message = ex.Message;
                            _logger.LogCritical(ex, "Lỗi nghiêm trọng trong quá trình chạy Background Sync.");
                        }

                        sw.Stop();
                        var level = status == "SUCCESS" ? LogLevel.Information : LogLevel.Error;
                        _logger.Log(level,
                            "========== [RUN {RunId}] KẾT THÚC | Trạng thái: {Status} | Thời gian: {Elapsed:F1}s | Kết quả: {Message} ==========",
                            runId, status, sw.Elapsed.TotalSeconds, message);
                    }
                }
            }
        }
    }
}
