using System;

namespace TitokVideos.Shared.Models
{
    public class BOProcessResult
    {
        public bool OK { get; set; } = false;
        public object? Result { get; set; }
        public int ErrorCode { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
