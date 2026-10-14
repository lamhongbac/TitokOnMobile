using Serilog;
using System.IO;
using System;
using TitokVideos.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// Khi chạy như Windows Service, thư mục mặc định là System32. 
// Phải chuyển về thư mục chứa file .exe để ghi file Log được.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

try 
{
    var builder = Host.CreateApplicationBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug() // Bật log chi tiết để dễ debug
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "Logs", "worker-log-.txt"), rollingInterval: RollingInterval.Day)
        .CreateLogger();
    builder.Services.AddSerilog();

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "TiktokSyncWorkerService";
    });

    // Đăng ký Worker
    builder.Services.AddHostedService<TiktokSyncWorker>();

    // Chỉ cần HttpClient để gọi API, không cần BLL/DAL
    builder.Services.AddHttpClient();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    // Cứu cánh cuối cùng nếu crash ngay lúc khởi động
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fatal_startup_error.txt"), ex.ToString());
    throw;
}
