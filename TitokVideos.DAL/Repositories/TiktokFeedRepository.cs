using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using TitokVideos.DAL.Entities;

namespace TitokVideos.DAL.Repositories
{
    public class TiktokFeedRepository : ITiktokFeedRepository
    {
        private readonly IDbConnection _dbConnection;

        public TiktokFeedRepository(IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task<int> AddAsync(TiktokFeed entity)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            var sql = @"
                INSERT INTO [dbo].[TiktokFeed] 
                    (Id, VideoId, Title, ThumbnailUrl, MediaUrl, PublishedAt, ViewCount, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, IsActive)
                VALUES 
                    (@Id, @VideoId, @Title, @ThumbnailUrl, @MediaUrl, @PublishedAt, @ViewCount, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @IsActive);
            ";

            return await _dbConnection.ExecuteAsync(sql, entity);
        }

        public async Task<DateTimeOffset?> GetLatestPublishedAtAsync()
        {
            var sql = "SELECT TOP 1 PublishedAt FROM [dbo].[TiktokFeed] ORDER BY PublishedAt DESC;";
            return await _dbConnection.QueryFirstOrDefaultAsync<DateTimeOffset?>(sql);
        }

        public async Task<TiktokFeed?> GetByVideoIdAsync(string videoId)
        {
            var sql = "SELECT * FROM [dbo].[TiktokFeed] WHERE VideoId = @VideoId;";
            return await _dbConnection.QueryFirstOrDefaultAsync<TiktokFeed>(sql, new { VideoId = videoId });
        }

        public async Task<IEnumerable<TiktokFeed>> GetPagedAsync(int pageIndex, int pageSize)
        {
            var sql = @"
                SELECT * FROM [dbo].[TiktokFeed]
                ORDER BY PublishedAt DESC
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY;
            ";
            return await _dbConnection.QueryAsync<TiktokFeed>(sql, new { Offset = pageIndex * pageSize, PageSize = pageSize });
        }

        public async Task<IEnumerable<TiktokFeed>> GetOldThumbnailsAsync()
        {
            var sql = "SELECT * FROM [dbo].[TiktokFeed] WHERE ThumbnailUrl LIKE 'http%' OR MediaUrl LIKE 'http%';";
            return await _dbConnection.QueryAsync<TiktokFeed>(sql);
        }

        // New paginated version for DRY migration
        public async Task<IEnumerable<TiktokFeed>> GetOldThumbnailsPagedAsync(int pageIndex, int pageSize)
        {
            var sql = @"
                SELECT * FROM [dbo].[TiktokFeed]
                WHERE ThumbnailUrl LIKE 'http%' OR MediaUrl LIKE 'http%'
                ORDER BY PublishedAt DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
            return await _dbConnection.QueryAsync<TiktokFeed>(sql, new { Offset = pageIndex * pageSize, PageSize = pageSize });
        }

        public async Task<int> UpdateThumbnailUrlAsync(Guid id, string url)
        {
            var sql = "UPDATE [dbo].[TiktokFeed] SET ThumbnailUrl = @Url WHERE Id = @Id;";
            return await _dbConnection.ExecuteAsync(sql, new { Url = url, Id = id });
        }

        public async Task<int> UpdateMediaUrlAsync(Guid id, string url)
        {
            var sql = "UPDATE [dbo].[TiktokFeed] SET MediaUrl = @Url WHERE Id = @Id;";
            return await _dbConnection.ExecuteAsync(sql, new { Url = url, Id = id });
        }
    }
}
