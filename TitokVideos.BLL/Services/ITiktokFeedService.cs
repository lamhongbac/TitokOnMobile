using System.Threading.Tasks;
using TitokVideos.Shared.Models;

namespace TitokVideos.BLL.Services
{
    public interface ITiktokFeedService
    {
        Task<BOProcessResult> SyncTiktokFeedsAsync();
        Task<BOProcessResult> GetFeedsAsync(int pageIndex, int pageSize);
        Task<BOProcessResult> MigrateOldThumbnailsAsync();
    }
}
