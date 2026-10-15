using System;

namespace TitokVideos.Shared.Models
{
    /// <summary>
    /// Nguồn kích hoạt thao tác (dùng cho báo cáo download).
    /// GUI và Worker gửi giá trị này qua header <see cref="HeaderName"/> khi gọi API.
    /// </summary>
    public static class TriggerSource
    {
        /// <summary>Tên HTTP header mang nguồn kích hoạt.</summary>
        public const string HeaderName = "X-Trigger-Source";

        /// <summary>Thao tác thủ công từ Web GUI.</summary>
        public const string ManualGui = "Manual-WebGUI";

        /// <summary>Thao tác tự động từ Background Worker.</summary>
        public const string Worker = "Worker";

        /// <summary>Không xác định (gọi API trực tiếp, Swagger, Postman...).</summary>
        public const string Unknown = "Unknown-API";

        /// <summary>Chuẩn hóa giá trị header về 1 trong các hằng số đã biết.</summary>
        public static string Normalize(string? value)
        {
            if (string.Equals(value, ManualGui, StringComparison.OrdinalIgnoreCase)) return ManualGui;
            if (string.Equals(value, Worker, StringComparison.OrdinalIgnoreCase)) return Worker;
            return Unknown;
        }
    }
}
