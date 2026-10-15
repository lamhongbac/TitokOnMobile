using System;

namespace TitokVideos.Shared.Models
{
    public class BOProcessResult
    {
        public bool OK { get; set; } = false;
        public virtual object? Result { get; set; }
        public int ErrorCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool HasNextPage { get; set; } = false;
    }

    public class BOProcessResult<T> : BOProcessResult
    {
        private T? _result;

        public new T? Result
        {
            get => _result;
            set
            {
                _result = value;
                base.Result = value;
            }
        }
    }
}
