# THẢO LUẬN TÍCH HỢP TIKTOK API CHO MOBILE APP

## 1. Bối cảnh (Context)
- **Mục tiêu ban đầu**: User muốn điều chỉnh Backend API của TikTok Feed để trả về cấu trúc giống hệt tài liệu `GiGa_Youtobe_API.docx` (dành cho YouTube cũ) với mong muốn đội Mobile (Android/Swift) ít phải sửa đổi code nhất có thể.
- **Tài liệu YouTube API cũ (`GiGa_Youtobe_API.docx`)**:
  - Request: `{ "numOfRows": "20", "pageToken": "" }`
  - Response: Chứa mảng `youTubeVideoDetails` (có `videoId`, `description`, `title`, `channelTitle`, `publicationDate`), và token phân trang (`prePageToken`, `nextPageToken`).
  - Không có trường `thumbnailUrl`. 

## 2. Phân tích các "điểm nghẽn" (Bottlenecks) nếu cố ép API thành chuẩn YouTube
Thông qua trao đổi, AI đã chỉ ra 4 vấn đề chí mạng nếu chỉ sửa API mà không yêu cầu Mobile cập nhật giao diện/logic:

1. **Vấn đề Ảnh bìa (Thumbnail)**:
   - *YouTube*: Mobile tự sinh link ảnh từ `videoId` (vd: `img.youtube.com/vi/{videoId}/hqdefault.jpg`).
   - *TikTok*: Ảnh bìa là link CDN có mã băm bảo mật (`https://p16-sign-sg.tiktokcdn.com/...`). Không thể đoán. Nếu API mới không trả trường URL ảnh và Mobile không sửa logic load ảnh, toàn bộ Feed sẽ lỗi trắng/hỏng ảnh bìa.

2. **Vấn đề Tỷ lệ khung hình (Aspect Ratio)**:
   - *YouTube*: Khung hình ngang 16:9. Layout danh sách trên mobile đang bám theo tỷ lệ này.
   - *TikTok*: Khung hình dọc 9:16. Load vào UI hiện tại sẽ bị crop hoặc dư viền đen lớn. Cần điều chỉnh Grid/List Layout.

3. **Vấn đề Trình phát (Video Player)**:
   - *YouTube*: Mobile khả năng cao đang dùng SDK Player Native của YouTube. Nhét ID của TikTok vào SDK này sẽ làm app crash hoặc báo lỗi.
   - *TikTok*: Buộc phải tạo luồng riêng mở **WebView** tải URL `https://www.tiktok.com/embed/v2/{videoId}`.

4. **Vấn đề Mở liên kết (Deep Link)**:
   - Các intent mở link `https://www.youtube.com/watch?v=` bằng app ngoại sẽ không có tác dụng với TikTok ID.

## 3. Hướng giải quyết & Kế hoạch tiếp theo
- Dựa trên đề xuất của User, để có thể đưa ra giải pháp "ít đau thương" nhất cho cả Backend và Mobile, **AI cần được xem Source Code trực tiếp của 2 nền tảng Mobile (Android - Kotlin/Java và iOS - Swift)**.
- Khi có source code, AI sẽ kiểm tra:
  - Mobile đang parse JSON như thế nào?
  - Logic tự sinh Thumbnail hiện tại nằm ở đâu?
  - Giao diện Player đang dùng công nghệ gì (Native, SDK thứ 3, hay WebView)?
- **Kết quả kỳ vọng**: Từ kết quả phân tích source code Mobile, AI sẽ đề xuất cấu trúc API Backend hoàn thiện cuối cùng và chỉ ra chính xác các dòng code Mobile cần sửa để tích hợp TikTok.

## 4. Hướng dẫn sử dụng file này cho phiên làm việc (Session) tiếp theo
Khi User mở lại phiên làm việc trên máy khác, chỉ cần gửi câu lệnh sau cho AI:
> *"Tôi muốn tiếp tục thảo luận về tích hợp TikTok cho Mobile. Hãy đọc file `D:\GIT PROJECT\TitokVideo\TitokVideos\Docs\TikTok_Mobile_Integration_Discussion.md` để nắm bối cảnh, sau đó tôi sẽ cung cấp source code Mobile cho bạn phân tích."*
