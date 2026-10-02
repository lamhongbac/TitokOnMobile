using System;

namespace TitokVideos.BLL.BOs
{
    public class TiktokFeedBO
    {
        public Guid Id { get; set; }
        public string VideoId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string MediaUrl { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        public int ViewCount { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
