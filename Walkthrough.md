# Backend Walkthrough (03-Oct-2026)

## Tổng quan
Quá trình nâng cấp Backend API (`TitokVideos.API`) để hỗ trợ đồng bộ danh sách video TikTok đã hoàn tất. Endpoint `/api/v1/tiktok/videos` đã được điều chỉnh để hỗ trợ phân trang Mobile chuẩn xác nhất.

## Chi tiết các thay đổi

### 1. Nâng cấp Generic Response Model
- **File**: `TitokVideos.Shared/Models/BOProcessResult.cs`
- **Thay đổi**: Thêm thuộc tính `HasNextPage` (kiểu `bool`).
- **Lý do**: App iOS cần biết chính xác có nên trigger hàm LoadMore khi vuốt xuống đáy danh sách (infinite scrolling) hay không, việc bổ sung biến flag này ở lớp Model gốc đảm bảo JSON trả về luôn chuẩn format được thống nhất.

### 2. Tối ưu thuật toán Phân trang (Pagination)
- **File**: `TitokVideos.BLL/Services/TiktokFeedService.cs`
- **Thay đổi**: Cập nhật method `GetFeedsAsync` ứng dụng kỹ thuật **Fetch `pageSize + 1`**.
- **Lý do**: Thay vì gọi thêm 1 câu lệnh `COUNT(*)` tốn kém để đếm tổng số trang, Backend sẽ lấy dư 1 bản ghi (ví dụ client yêu cầu 10, Backend lấy 11). Nếu Database trả về đủ 11 bản ghi, hệ thống tự hiểu `HasNextPage = true` và cắt bỏ bản ghi thứ 11 trước khi trả JSON về cho Mobile. Kỹ thuật này giúp API phản hồi cực nhanh trên dữ liệu lớn.

### 3. Kết quả JSON trả ra
Kết quả JSON trả về cho dự án Mobile hiện tại đã đảm bảo cấu trúc:
```json
{
  "ok": true,
  "result": [ ... list of videos ... ],
  "errorCode": 0,
  "message": "Success",
  "hasNextPage": true
}
```
Cấu trúc này đã được map tương thích 100% với Mobile Model (`TikTokResponseModel.swift`).
