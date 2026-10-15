using Microsoft.Data.SqlClient;
using System.Data;
using TitokVideos.BLL.Services;
using TitokVideos.DAL.Repositories;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/api-log-.txt", rollingInterval: RollingInterval.Day)
    // Download report: file riêng nằm trong project API (Logs/download-report-yyyyMMdd.txt).
    // Mọi thao tác tải media từ GUI (Manual Sync/Migrate) và Worker (Auto Sync) đều đi qua API nên đều được ghi vào đây.
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Serilog.Filters.Matching.FromSource(TiktokFeedService.DownloadReportCategory))
        .WriteTo.File(
            Path.Combine(builder.Environment.ContentRootPath, "Logs", "download-report-.txt"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] TRIGGER={TriggeredBy} | {Message:lj}{NewLine}"))
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Cấu hình CORS để cho phép gọi chéo domain từ GUI hoặc app khác
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Cấu hình DI
builder.Services.AddScoped<IDbConnection>(sp => 
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<ITiktokFeedRepository, TiktokFeedRepository>();
builder.Services.AddScoped<ITiktokFeedService, TiktokFeedService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Cấu hình Swagger/OpenAPI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();
