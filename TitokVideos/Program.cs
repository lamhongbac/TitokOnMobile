

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;

// 1. ÉP WINDOWS CONSOLE HIỂN THỊ CHUẨN UTF-8 (TIẾNG VIỆT)
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("Đang gọi API lấy danh sách video của Gigamall Vietnam...");

using var client = new HttpClient();
var request = new HttpRequestMessage
{
    Method = HttpMethod.Get,
    RequestUri = new Uri("https://tiktok-api23.p.rapidapi.com/api/user/posts?secUid=MS4wLjABAAAAIfCOLpBXIExhLB0JP2lEainL6e3N1tQNvmaVW-s0adv_-_Tdt21-wxKXKhj-49qE"),
    Headers =
    {
        { "x-rapidapi-host", "tiktok-api23.p.rapidapi.com" },
        { "x-rapidapi-key", "f5836d2ed0msh35455f28992a930p1d0f9fjsnf0faf50ad058" },
    }
};

try
{
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    // Lấy chuỗi raw từ API (Lúc này tiếng Việt có thể đang ở dạng \u....)
    var rawBody = await response.Content.ReadAsStringAsync();

    // 2. CẤU HÌNH BỘ GIẢI MÃ JSON KHÔNG ESCAPE TIẾNG VIỆT
    var options = new JsonSerializerOptions
    {
        // Cho phép hiển thị tất cả các bảng mã Unicode (bao gồm tiếng Việt)
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true // Định dạng JSON thụt lề cho dễ nhìn
    };

    // Phân tích cú pháp chuỗi raw và ép kiểu lại với cấu hình tùy chỉnh
    using JsonDocument doc = JsonDocument.Parse(rawBody);
    string prettyVietnameseJson = JsonSerializer.Serialize(doc.RootElement, options);

    Console.WriteLine("THÀNH CÔNG! Dưới đây là dữ liệu đã bảo toàn tiếng Việt:");
    Console.WriteLine(prettyVietnameseJson);
}
catch (Exception ex)
{
    Console.WriteLine($"LỖI GỌI API: {ex.Message}");
}