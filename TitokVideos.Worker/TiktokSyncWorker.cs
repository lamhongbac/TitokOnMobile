using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TitokVideos.BLL.Services;

namespace TitokVideos.Worker
{
    public class TiktokSyncWorker : BackgroundService
    {
        private readonly ILogger<TiktokSyncWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public TiktokSyncWorker(ILogger<TiktokSyncWorker> logger, IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
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
                    _logger.LogInformation($"[{DateTime.Now:HH:mm:ss}] Starting Tiktok Feed Smart Sync...");
                    
                    try
                    {
                        // Tạo DI Scope mới để lấy ra các Service Scoped
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var syncService = scope.ServiceProvider.GetRequiredService<ITiktokFeedService>();
                            var result = await syncService.SyncTiktokFeedsAsync();
                            
                            if (result.OK)
                            {
                                _logger.LogInformation($"[SUCCESS] {result.Message}");
                            }
                            else
                            {
                                _logger.LogError($"[FAILED] {result.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "Lỗi nghiêm trọng trong quá trình chạy Background Sync.");
                    }
                }
            }
        }
    }
}
