# BÀI TOÁN TÍCH HỢP TIKTOK FEED - TECHNICAL HANDOVER DOCUMENT

## 1. TỔNG QUAN KIẾN TRÚC HỆ THỐNG
Hệ thống được thiết kế theo chuẩn **Clean Architecture** bao gồm 5 dự án (projects) chính giúp tách biệt hoàn toàn Logic, Database và Giao diện:

- **TitokVideos.Shared**: Chứa các Entity cơ sở (`BaseEntity`) và các đối tượng Data Transfer Object dùng chung (Chuẩn hóa `BOProcessResult` trả về cho mọi API/Service).
- **TitokVideos.DAL**: Tầng Data Access Layer. Kết nối CSDL SQL Server bằng `Dapper`. Gồm các Repository truy xuất trực tiếp Database.
- **TitokVideos.BLL**: Tầng Business Logic Layer. Xử lý thuật toán **"Smart Sync"** (chỉ lấy các video mới chưa có trong hệ thống bằng cách so sánh mốc thời gian `publishedAt`), quản lý cơ chế bắt lỗi và logging tập trung (Serilog).
- **TitokVideos.GUI (Web Admin)**: Giao diện ASP.NET Core MVC dành cho Admin. Cho phép đồng bộ thủ công, phân trang và đính kèm trang **Mobile Simulator** để test thử luồng hoạt động của App.
- **TitokVideos.API**: RESTful API cung cấp dữ liệu JSON chuẩn cho hệ thống Mobile CRM App.
- **TitokVideos.Worker**: Background Job (Worker Service) chạy độc lập, tự động kích hoạt tiến trình đồng bộ vào một khung giờ cố định hằng ngày.

### Công nghệ nền tảng
- **Backend Framework**: .NET 9
- **Cơ sở dữ liệu**: SQL Server (Dapper ORM)
- **Ghi log (Tracing)**: Serilog (Xuất log ra file dạng xoay vòng hàng ngày trong thư mục `Logs/`).
- **External Data Source**: TikTok API (thông qua RapidAPI).

---

## 2. QUẢN LÝ CẤU HÌNH (CONFIGURATION)
Mọi thông số động của hệ thống được quản lý bảo mật thông qua các file `appsettings.json` tương ứng tại 3 dự án (GUI, API, Worker):

- **Chuỗi kết nối DB:** Nằm trong mục `ConnectionStrings:DefaultConnection`.
- **Tham số API TikTok:** Nằm trong block `RapidApi`. Có thể linh hoạt đổi `Host`, `Key`, và `SecUid` của người dùng mà không cần re-compile code.
- **Tham số Background Job (Dự án Worker):** Nằm trong block `SyncJob`, cho phép đổi lịch chạy hằng ngày (`RunAtHour`, `RunAtMinute`).

---

## 3. TÀI LIỆU DÀNH CHO ĐỘI MOBILE APP (ANDROID & iOS/SWIFT)

### A. API Specification
- **Endpoint**: `GET /api/v1/tiktok/videos`
- **Mục đích**: Lấy danh sách video TikTok đã lưu trong CSDL nội bộ.
- **Tham số truyền vào (Query Params)**:
  - `pageIndex` (int): Vị trí trang (bắt đầu từ `0`). Mặc định: `0`.
  - `pageSize` (int): Số lượng video trên mỗi trang. Mặc định: `10` (Bảo mật tối đa: `50`).
- **Response Format (JSON)**:
```json
{
  "ok": true,
  "result": [
    {
      "id": 1,
      "videoId": "7132984123491...",
      "title": "Mô tả video tiktok",
      "thumbnailUrl": "https://p16-sign-sg.tiktokcdn.com/... (Link ảnh bìa)",
      "mediaUrl": "https://v16-webapp-prime.tiktok.com/... (Link MP4 - XEM LƯU Ý BÊN DƯỚI)",
      "publishedAt": "2026-10-02T15:33:36",
      "viewCount": 1500,
      "isActive": true
    }
  ],
  "message": "Success"
}
```

### B. Hướng dẫn Tích hợp & Các Lưu ý Đặc biệt Quan trọng (CRITICAL)

**1. KHÔNG sử dụng Native Player cho trường `mediaUrl`**
Do cơ chế chống Hotlinking của CDN mạng TikTok, URL chứa trong trường `mediaUrl` bao gồm token chữ ký số bảo mật `x-expires`. Sau một thời gian, token này hết hạn.
Nếu Mobile App truyền link này thẳng vào trình phát video native (ExoPlayer trên Android hoặc AVPlayer trên Swift), hệ thống của Akamai sẽ chặn và trả về lỗi **403 Access Denied**.

**2. Giải pháp chuẩn (Best Practice): Phát Video qua In-App Browser (WebView)**
Để đảm bảo video luôn xem được trơn tru, đội Mobile phải sử dụng Web Player nhúng chính thức của TikTok:
- Ở màn hình **Danh sách Video (List/Grid)**: Sử dụng trường `thumbnailUrl` để render ảnh bìa.
- Khi người dùng **Click xem một Video**: Chuyển sang màn hình mới có một **WebView (In-App Browser)**.
- **Cấu trúc URL cho WebView**: 
  Hãy dùng chuỗi: `https://www.tiktok.com/embed/v2/{videoId}`
  
**Ví dụ code Swift (iOS):**
```swift
import WebKit
// Lấy videoId từ API trả về
let videoId = videoData.videoId
let embedUrl = URL(string: "https://www.tiktok.com/embed/v2/\(videoId)")!

let webView = WKWebView(frame: self.view.bounds)
webView.load(URLRequest(url: embedUrl))
self.view.addSubview(webView)
```

**Ví dụ code Android (Kotlin):**
```kotlin
val webView = findViewById<WebView>(R.id.webView)
webView.settings.javaScriptEnabled = true // Rất quan trọng để chạy player TikTok

val videoId = videoData.videoId
webView.loadUrl("https://www.tiktok.com/embed/v2/$videoId")
```

**3. Quản lý LifeCycle của WebView**
Khi người dùng đóng màn hình xem video (Back ra ngoài), App phải lập tức hủy WebView hoặc set URL thành rỗng (`webView.loadUrl("about:blank")`). Nếu bỏ quên, âm thanh từ video TikTok sẽ tiếp tục phát ngầm gây lỗi trải nghiệm nghiệm trọng cho End-user.
