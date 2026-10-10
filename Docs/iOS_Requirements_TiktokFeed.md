# TÀI LIỆU YÊU CẦU TÍCH HỢP TIKTOK FEED CHO iOS APP

## 1. MỤC TIÊU
Tài liệu cung cấp thông số API và các hướng dẫn kỹ thuật để team iOS (Swift) tiến hành thiết kế giao diện và tích hợp dữ liệu, nhằm hiển thị danh sách video Tiktok Feed lên ứng dụng.

## 2. THÔNG TIN API
Dựa trên cấu hình hệ thống hiện tại, API lấy danh sách video đã được triển khai với các thông số sau:

- **Base URL**: `http://115.165.166.2:8087`
- **Endpoint**: `GET /api/v1/tiktok/videos`
- **Full URL**: `http://115.165.166.2:8087/api/v1/tiktok/videos`
- **Mục đích**: Lấy danh sách video TikTok đã đồng bộ về CSDL để hiển thị trên app.

### Tham số truyền vào (Query Parameters):
| Tham số | Kiểu dữ liệu | Mặc định | Mô tả |
| :--- | :--- | :--- | :--- |
| `pageIndex` | Int | `0` | Vị trí trang, bắt đầu từ `0`. Dùng cho phân trang. |
| `pageSize` | Int | `10` | Số lượng video lấy về trên mỗi trang (Không nên vượt quá `50`). |

### Response Format (JSON):
```json
{
  "ok": true,
  "result": [
    {
      "id": 1,
      "videoId": "7132984123491...",
      "title": "Mô tả video tiktok",
      "thumbnailUrl": "https://p16-sign-sg.tiktokcdn.com/... ",
      "mediaUrl": "https://v16-webapp-prime.tiktok.com/... ",
      "publishedAt": "2026-10-02T15:33:36",
      "viewCount": 1500,
      "isActive": true
    }
  ],
  "message": "Success"
}
```

## 3. YÊU CẦU VỀ UI/UX
1. **Màn hình Danh sách Video (Feed/Grid/List)**:
   - Sử dụng trường `thumbnailUrl` để tải và hiển thị ảnh bìa (thumbnail) của video.
   - Có thể kết hợp hiển thị `title` hoặc `viewCount` tuỳ thuộc vào bản vẽ thiết kế (Figma).
   - Tích hợp **Load More / Infinite Scrolling**: Gọi API với `pageIndex` tăng dần khi người dùng cuộn đến cuối danh sách.
2. **Màn hình Chi tiết Video**:
   - Khi người dùng chọn vào 1 video bất kỳ, mở màn hình mới (Video Player) để phát video đó.

---

## 4. HƯỚNG DẪN KỸ THUẬT & LƯU Ý QUAN TRỌNG (CRITICAL)

Có một số giới hạn khắt khe từ cơ chế chống Hotlinking của mạng TikTok mà Dev iOS **CẦN TUÂN THỦ NGHIÊM NGẶT** để tránh lỗi không xem được video:

> [!WARNING]
> 1. **TUYỆT ĐỐI KHÔNG dùng AVPlayer (Native Player) với `mediaUrl`**: Link trong trường `mediaUrl` có chứa token chữ ký số (`x-expires`) và sẽ hết hạn rất nhanh. TikTok đồng thời chặn Referer khắt khe. Nếu đưa vào AVPlayer sẽ bị lỗi HTTP 403 Access Denied hoặc không hỗ trợ Source.
> 2. **KHÔNG DÙNG API Embed `embed/v2`**: Endpoint URL dạng `https://www.tiktok.com/embed/v2/...` đang bị chặn tự động bởi tường lửa (WAF) của Akamai (lỗi 503).

### Giải pháp bắt buộc: Sử dụng `WKWebView` với Player v1
Giao diện phát video cần tích hợp một In-App Browser (`WKWebView`) với API Web Player v1 chính thức của TikTok.

**Cấu trúc URL chuẩn:**
`https://www.tiktok.com/player/v1/{videoId}?music_info=1&description=1&autoplay=1&mute=0`

**Mã nguồn Swift tham khảo:**
```swift
import WebKit

// Lấy videoId từ Model trả về của API
let videoId = videoData.videoId
let playerUrlString = "https://www.tiktok.com/player/v1/\(videoId)?music_info=1&description=1&autoplay=1&mute=0"

guard let playerUrl = URL(string: playerUrlString) else { return }

// Cấu hình cho phép Autoplay âm thanh trên WebView
let webConfiguration = WKWebViewConfiguration()
webConfiguration.allowsInlineMediaPlayback = true
webConfiguration.mediaTypesRequiringUserActionForPlayback = [] 

let webView = WKWebView(frame: self.view.bounds, configuration: webConfiguration)
webView.load(URLRequest(url: playerUrl))
self.view.addSubview(webView)
```

> [!CAUTION]
> ### Quản lý LifeCycle của WKWebView
> Khi người dùng bấm nút **Back** để thoát khỏi màn hình phát video, bạn **PHẢI LẬP TỨC** xoá nội dung WebView hoặc ngắt luồng tải (`load("about:blank")`). Nếu không, luồng âm thanh của Video TikTok vẫn sẽ tiếp tục phát dưới nền (Background) gây ra trải nghiệm tồi tệ.

**Cách xử lý tham khảo khi thoát màn hình:**
```swift
override func viewWillDisappear(_ animated: Bool) {
    super.viewWillDisappear(animated)
    // Tải một trang trắng để ngừng âm thanh ngay lập tức
    webView.load(URLRequest(url: URL(string: "about:blank")!))
    
    // Tuỳ vào kiến trúc view, có thể gọi thêm:
    // webView.removeFromSuperview()
}
```
