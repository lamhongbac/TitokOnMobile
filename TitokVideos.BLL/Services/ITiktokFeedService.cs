using System.Threading.Tasks;
using TitokVideos.Shared.Models;

namespace TitokVideos.BLL.Services
{
    public interface ITiktokFeedService
    {
        Task<BOProcessResult> SyncTiktokFeedsAsync(string triggeredBy = TriggerSource.Unknown);
        Task<BOProcessResult> GetFeedsAsync(int pageIndex, int pageSize);
        Task<BOProcessResult> MigrateOldThumbnailsAsync(int pageIndex, int pageSize, string triggeredBy = TriggerSource.Unknown);
    }
}
