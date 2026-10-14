using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using TitokVideos.BLL.Services;
using TitokVideos.Shared.Models;

namespace TitokVideos.API.Controllers
{
    [ApiController]
    [Route("api/v1/tiktok/videos")]
    public class TiktokFeedController : ControllerBase
    {
        private readonly ITiktokFeedService _service;

        public TiktokFeedController(ITiktokFeedService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy danh sách video TikTok đã lưu trong cơ sở dữ liệu
        /// </summary>
        /// <param name="pageIndex">Trang hiện tại (bắt đầu từ 0)</param>
        /// <param name="pageSize">Số lượng bản ghi trên một trang</param>
        /// <returns>BOProcessResult chứa danh sách TiktokFeedBO</returns>
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int pageIndex = 0, [FromQuery] int pageSize = 10)
        {
            // Validation an toàn để tránh tải quá nhiều dữ liệu
            if (pageSize > 50) pageSize = 50;
            if (pageIndex < 0) pageIndex = 0;

            var result = await _service.GetFeedsAsync(pageIndex, pageSize);
            
            // Trả về JSON bọc trong định dạng BOProcessResult chuẩn
            return Ok(result);
        }

        /// <summary>
        /// Đồng bộ video mới từ TikTok (Smart Sync)
        /// </summary>
        /// <returns>Kết quả đồng bộ</returns>
        [HttpPost("sync")]
        public async Task<IActionResult> Sync()
        {
            var result = await _service.SyncTiktokFeedsAsync();
            return Ok(result);
        }

        /// <summary>
        /// Migrate (tải lại) các thumbnail cũ chưa được lưu trên server
        /// </summary>
        /// <returns>Kết quả migrate</returns>
        [HttpPost("migrate-thumbnails")]
        public async Task<IActionResult> MigrateThumbnails([FromQuery] int pageIndex = 0, [FromQuery] int pageSize = 10)
        {
            var result = await _service.MigrateOldThumbnailsAsync(pageIndex, pageSize);
            return Ok(result);
        }
    }
}
