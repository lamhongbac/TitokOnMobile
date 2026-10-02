using System;
using TitokVideos.Shared.Entities;

namespace TitokVideos.DAL.Entities
{
    public class TiktokFeed : BaseEntity<Guid>
    {
        public string VideoId { get; set; }
        public string? Title { get; set; }
        public string ThumbnailUrl { get; set; }
        public string MediaUrl { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public int ViewCount { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
