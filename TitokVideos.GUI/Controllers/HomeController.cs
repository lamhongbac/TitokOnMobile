using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TitokVideos.BLL.BOs;
using TitokVideos.GUI.Models;
using TitokVideos.Shared.Models;

namespace TitokVideos.GUI.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly string _apiBaseUrl;

        public HomeController(ILogger<HomeController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _apiBaseUrl = _configuration["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        }

        public async Task<IActionResult> Index(int page = 0)
        {
            int pageSize = _configuration.GetValue<int>("Pagination:PageSize", 5);
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_apiBaseUrl}/api/v1/tiktok/videos?pageIndex={page}&pageSize={pageSize}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BOProcessResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var feeds = result?.Result as IEnumerable<TiktokFeedBO>;

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.HasNext = feeds != null && feeds.Count() == pageSize;

            return View(feeds);
        }

        [HttpPost]
        public async Task<IActionResult> Sync()
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync($"{_apiBaseUrl}/api/v1/tiktok/videos/sync", null);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BOProcessResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            TempData["Message"] = result?.Message;
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> MigrateThumbnails(int page = 0)
        {
            int pageSize = _configuration.GetValue<int>("Pagination:PageSize", 5);
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync($"{_apiBaseUrl}/api/v1/tiktok/videos/migrate-thumbnails?pageIndex={page}&pageSize={pageSize}", null);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BOProcessResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            TempData["Message"] = result?.Message;
            return RedirectToAction("Index", new { page });
        }

        // --- MOBILE APP SIMULATION ENDPOINTS ---
        public IActionResult MobileSimulator()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
