using System;

namespace TitokVideos.Shared.Entities
{
    public abstract class BaseEntity<TId>
    {
        public TId Id { get; set; } = default!;
        public string CreatedBy { get; set; } = "system";
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string ModifiedBy { get; set; } = "system";
        public DateTime ModifiedOn { get; set; } = DateTime.Now;
    }
}
