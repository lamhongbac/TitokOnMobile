using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using TitokVideos.BLL.Services;
using TitokVideos.BLL.BOs;
using TitokVideos.GUI.Models;

namespace TitokVideos.GUI.Controllers;

public class HomeController : Controller
{
    private readonly ITiktokFeedService _service;
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;

    public HomeController(ITiktokFeedService service, ILogger<HomeController> logger, IConfiguration configuration)
    {
        _service = service;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index(int page = 0)
    {
        int pageSize = _configuration.GetValue<int>("Pagination:PageSize", 5);
        var result = await _service.GetFeedsAsync(page, pageSize);
        
        var feeds = result.Result as IEnumerable<TiktokFeedBO>;
        
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.HasNext = feeds != null && feeds.Count() == pageSize;

        return View(feeds);
    }

    [HttpPost]
    public async Task<IActionResult> Sync()
    {
        var result = await _service.SyncTiktokFeedsAsync();
        TempData["Message"] = result.Message;
        return RedirectToAction("Index");
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
