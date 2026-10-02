using Microsoft.Data.SqlClient;
using System.Data;
using TitokVideos.BLL.Services;
using TitokVideos.DAL.Repositories;
using TitokVideos.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/worker-log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Services.AddSerilog();

// Đăng ký Worker
builder.Services.AddHostedService<TiktokSyncWorker>();

// Đăng ký DI (Dapper Connection, Repositories, Services)
builder.Services.AddScoped<IDbConnection>(sp => 
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<ITiktokFeedRepository, TiktokFeedRepository>();
builder.Services.AddScoped<ITiktokFeedService, TiktokFeedService>();
builder.Services.AddHttpClient();

var host = builder.Build();
host.Run();
