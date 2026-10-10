using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TitokVideos.DAL.Entities;

namespace TitokVideos.DAL.Repositories
{
    public interface ITiktokFeedRepository
    {
        Task<int> AddAsync(TiktokFeed entity);
        Task<DateTimeOffset?> GetLatestPublishedAtAsync();
        Task<TiktokFeed?> GetByVideoIdAsync(string videoId);
        Task<IEnumerable<TiktokFeed>> GetPagedAsync(int pageIndex, int pageSize);
        Task<IEnumerable<TiktokFeed>> GetOldThumbnailsAsync();
        Task<int> UpdateThumbnailUrlAsync(Guid id, string url);
        Task<int> UpdateMediaUrlAsync(Guid id, string url);
    }
}
