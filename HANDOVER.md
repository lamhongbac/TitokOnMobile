# HANDOVER Documentation

## Business Logic: `TiktokFeedService`

The **TiktokFeedService** encapsulates all business rules for synchronising TikTok video feeds, serving paginated feed data, and migrating legacy URLs to locally stored media.  The class follows a **DRY** approach – shared constants, a core download helper, and a URL‑conversion utility are used across public operations.

---
### Public API

| Method | Signature | Summary |
|--------|-----------|---------|
| **`SyncTiktokFeedsAsync`** | `public async Task<BOProcessResult> SyncTiktokFeedsAsync(string triggeredBy = TriggerSource.Unknown)` | Performs a *smart sync*: fetches the latest videos from RapidAPI, downloads **thumbnail** and **MP4** to `wwwroot/thumbnails` and `wwwroot/videos`, inserts new rows into `TiktokFeed` with **local URLs**.  If a download fails, the original TikTok URL is kept as a fallback so the feed remains usable.  `triggeredBy` is written to the Download Report.
| **`GetFeedsAsync`** | `public async Task<BOProcessResult> GetFeedsAsync(int pageIndex, int pageSize)` | Retrieves a paginated list of feeds.  Converts stored **local URLs** (`/thumbnails/...`, `/videos/...`) to fully qualified public URLs by prepending `ApiSettings:BaseUrl` via the shared `ToPublicUrl` helper.  Returns the result wrapped in `BOProcessResult`.
| **`MigrateOldThumbnailsAsync`** | `public async Task<BOProcessResult> MigrateOldThumbnailsAsync(int pageIndex, int pageSize, string triggeredBy = TriggerSource.Unknown)` | Scans the database (paginated) for rows whose **ThumbnailUrl** or **MediaUrl** still contain TikTok HTTP URLs, re‑fetches fresh URLs from RapidAPI, downloads **only the parts that are still remote** (parts already local are `SKIPPED`), and updates the DB fields with the new local URLs.  An item is counted as failed if **thumbnail or video** fails. |

---
### Shared Constants

```csharp
private const string ThumbnailUrlPrefix = "/thumbnails/"; // local path prefix for thumbnails
private const string VideoUrlPrefix      = "/videos/";      // local path prefix for videos
```
These constants are the single source of truth for the local URL structure used by **Sync**, **Migrate**, and **GetFeeds**.

---
### Private Helpers (single source of truth)

| Helper | Signature | Purpose |
|--------|-----------|---------|
| **`ToPublicUrl`** | `private static string ToPublicUrl(string? baseUrl, string? url, string localPrefix)` | Prepends `baseUrl` to a local URL when the stored value starts with the supplied `localPrefix`.  Leaves external (http) URLs untouched.
| **`DownloadMediaAsync`** | `private async Task<LocalMedia> DownloadMediaAsync(HttpClient client, string operation, string videoId, string? coverUrl, string? playAddr, bool skipThumbnail = false, bool skipVideo = false)` | Core routine that attempts to download **both** thumbnail and video.  `operation` (`Sync`/`Migrate`) is written to the report.  Skip flags log `SKIPPED`; missing source URL logs `FAIL` with reason.  Returns a `LocalMedia` record (or `null` per part on failure).
| **`DownloadThumbnailAsync`** | `private async Task<string?> DownloadThumbnailAsync(HttpClient client, string operation, string videoId, string sourceUrl)` | Downloads the thumbnail image, writes it to `EnsureStoragePath(..., "thumbnails")`, and returns the relative path (`/thumbnails/{videoId}.jpg`).  Logs OK/FAIL to the Download Report.  Returns `null` on error.
| **`DownloadVideoAsync`** | `private async Task<string?> DownloadVideoAsync(HttpClient client, string operation, string videoId, string sourceUrl)` | Streams the MP4 file to `EnsureStoragePath(..., "videos")` and returns the relative path (`/videos/{videoId}.mp4`).  Logs OK/FAIL (with bytes) to the Download Report.  Returns `null` on error.
| **`EnsureStoragePath`** | `private string EnsureStoragePath(string configKey, string defaultFolder)` | Resolves the physical folder from configuration (`ApiSettings:ThumbnailStoragePath` / `ApiSettings:VideoStoragePath`) or defaults to `wwwroot/{folder}`.  Creates the folder if it does not exist.
| **`FetchRemoteVideosAsync`** | `private async Task<List<RemoteVideo>> FetchRemoteVideosAsync(HttpClient client)` | Calls RapidAPI to obtain the latest list of videos for the configured TikTok account and maps JSON to the internal `RemoteVideo` record.
| **`LocalMedia` record** | `private sealed record LocalMedia(string? ThumbnailUrl, string? MediaUrl)` | Simple DTO used by `DownloadMediaAsync` to convey the two possible local URLs.
| **`LogDownload` / `LogSkipped`** | `private void LogDownload(...)` / `private void LogSkipped(...)` | Write one `EVENT=Download` line per media file to the Download Report. |
| **`BeginReportScope`** | `private IDisposable? BeginReportScope(string? triggeredBy)` | Opens a log scope with property `TriggeredBy`; flows through async calls so every report line of the operation carries the trigger source. |
| **`DescribeFailure`** | `private static string DescribeFailure(Exception ex)` | Maps exceptions to human‑readable reasons (HTTP 403/404/410/429, network, timeout, invalid URL, permission, disk/IO). |
| **`IsRemoteUrl`** | `private static bool IsRemoteUrl(string? url)` | `true` when URL still points to TikTok (`http...`), used by Migrate to skip already‑local media. |

---
### Interaction Flow
1. **Sync** – `SyncTiktokFeedsAsync` → `FetchRemoteVideosAsync` → `DownloadMediaAsync` → `AddAsync` (DAL).
2. **Serve** – `GetFeedsAsync` reads from DAL, then `ToPublicUrl` converts stored paths to public URLs.
3. **Migrate** – `MigrateOldThumbnailsAsync(pageIndex, pageSize)` → `GetOldThumbnailsPagedAsync` (DAL) → `FetchRemoteVideosAsync` (fresh URLs) → `DownloadMediaAsync` → `UpdateThumbnailUrlAsync` / `UpdateMediaUrlAsync` (DAL).

All three public methods rely on the **same download logic** and **same URL‑prefix constants**, guaranteeing consistent behaviour across the system.

---

## Download Report Logging (added 2026‑10‑15)

Logging is implemented **inside `TiktokFeedService`** (BLL), so every caller — GUI (Manual) and Worker (Auto) — inherits it automatically because both go through the API.

### Log file
- Location: **`TitokVideos.API/Logs/download-report-yyyyMMdd.txt`** (rolling daily, under API `ContentRootPath`).
- Configured in `TitokVideos.API/Program.cs` via a Serilog sub‑logger filtered by category `TiktokFeedService.DownloadReportCategory` (`"DownloadReport"`).  `.Enrich.FromLogContext()` is required for `TriggeredBy`.
- The general `Logs/api-log-.txt` still receives all logs.

### Trigger source (`TitokVideos.Shared/Models/TriggerSource.cs`)
| Constant | Value | Sent by |
|----------|-------|---------|
| `HeaderName` | `X-Trigger-Source` | HTTP header name |
| `ManualGui` | `Manual-WebGUI` | GUI `HomeController.Sync` / `MigrateThumbnails` |
| `Worker` | `Worker` | `TiktokSyncWorker` |
| `Unknown` | `Unknown-API` | Direct API calls (Swagger/Postman) or unknown header |

`TiktokFeedController` reads the header with `[FromHeader]`, normalises it, and passes it to the service.

### Events
| Operation | EVENT | Content |
|-----------|-------|---------|
| Sync / Migrate | `Start` | Operation start (Migrate: page, pageSize) |
| Sync / Migrate | `Download` | Per file: VideoId, Type (Thumbnail/Video), Status (`OK`/`FAIL`/`SKIPPED`), Bytes, Elapsed, File, **Reason** |
| Migrate | `FetchFreshUrls` | RapidAPI fresh URL fetch OK (count) / FAIL (reason + impact) |
| Migrate | `Item` | Per video: Status, Thumbnail/Video status, `UrlSource` (`FRESH` / `STALE_DB` / `PARTIAL`) |
| Sync / Migrate | `End` | `SUCCESS` (counters + FailedIds), `FAILED`/`CRASHED` (reason), Migrate `NOTHING_TO_DO` |

Sample:
```
2026-10-15 09:15:41.007 +07:00 [WRN] TRIGGER=Manual-WebGUI | OPERATION=Migrate | EVENT=Download | VideoId=7298... | Type=Video | Status=FAIL | Bytes=0 | Elapsed=230ms | File=- | Reason=HTTP 403 Forbidden - URL TikTok đã hết hạn token (x-expires) hoặc bị chặn truy cập
```

### Failure reasons (`DescribeFailure`)
HTTP 403 (token `x-expires` hết hạn / bị chặn), 404 (video bị xóa/ẩn), 410 (link hết hạn), 429 (rate limit), other HTTP codes, network (DNS/SSL), timeout, invalid URL, no write permission, IO (disk full / file locked), missing source URL (no `cover`/`playAddr`).

---

## GUI: `HomeController` (API‑only architecture)

**Project:** `TitokVideos.GUI` — **không tham chiếu trực tiếp** tới BLL hay DAL.  Mọi thao tác đều gọi API (`TitokVideos.API`) qua `IHttpClientFactory`.

### Dependencies
- `IHttpClientFactory` — gửi HTTP request tới API.
- `IConfiguration` — đọc `ApiSettings:BaseUrl`, `Pagination:PageSize`.
- **Không có** `ITiktokFeedService`, `ITiktokFeedRepository`, `IDbConnection`.

### Actions

| Action | HTTP Method | API Endpoint | Summary |
|--------|-------------|--------------|---------|
| **`Index`** | GET | `GET /api/v1/tiktok/videos?pageIndex={page}&pageSize={size}` | Lấy danh sách video phân trang, hiển thị card grid với thumbnail, tiêu đề, lượt xem. Hỗ trợ phân trang (Quay lại / Tiếp tục). |
| **`Sync`** | POST | `POST /api/v1/tiktok/videos/sync` (header `X-Trigger-Source: Manual-WebGUI`) | Gọi API đồng bộ video mới từ TikTok. Kết quả hiển thị qua `TempData["Message"]`. |
| **`MigrateThumbnails`** | POST | `POST /api/v1/tiktok/videos/migrate-thumbnails?pageIndex={page}&pageSize={size}` (header `X-Trigger-Source: Manual-WebGUI`) | Migrate thumbnail/video cũ (URL TikTok → local) theo trang hiện tại. Kết quả hiển thị qua `TempData["Message"]`. |
| **`MobileSimulator`** | GET | — | Hiển thị trang mô phỏng Mobile WebView (iframe TikTok player). |
| **`Privacy`** | GET | — | Trang Privacy tĩnh. |
| **`Error`** | GET | — | Trang lỗi mặc định. |

> **Fix & Optimization (2026‑10‑15):**
> 1. **Generic Deserialization:** Đã bổ sung generic class `BOProcessResult<T> : BOProcessResult` trong `TitokVideos.Shared`. `HomeController.Index` nay deserialize trực tiếp `BOProcessResult<List<TiktokFeedBO>>`, strongly-typed an toàn và loại bỏ việc kiểm tra `JsonElement` thủ công.
> 2. **Sửa lỗi logic phân trang:** Sửa `ViewBag.HasNext = result?.HasNextPage ?? false;` (trước đây tính sai bằng `feeds.Count() == pageSize`, gây lỗi hiển thị nút "Tiếp tục" sang trang trống khi tổng số bản ghi chia hết cho `pageSize`).
> 3. **Header Compliance:** Đã rà soát và khẳng định 100% các request kích hoạt (`Sync`, `MigrateThumbnails`) từ GUI đều gửi kèm header `X-Trigger-Source: Manual-WebGUI`.

### View: `Index.cshtml`
- Hai nút hành động: **"Bắt đầu Đồng bộ (Smart Sync)"** và **"Migrate old Thumbnails"**.
- Card grid hiển thị video với thumbnail, tiêu đề, lượt xem, ngày đăng.
- Phân trang (Previous / Next) dùng `ViewBag.CurrentPage` và `ViewBag.HasNext` (chuẩn hóa từ `result.HasNextPage`).
- Modal popup mô phỏng Mobile WebView cho nút Review (nếu `TitokReview` = true).

---

## Worker: `TiktokSyncWorker` (Windows Service & API‑only architecture)

**Project:** `TitokVideos.Worker` — **không tham chiếu** tới BLL hay DAL (đã gỡ bỏ `ProjectReference`).  Chỉ phụ thuộc `TitokVideos.Shared` (cho `BOProcessResult` và `TriggerSource`).

### Kiến trúc Windows Service:
- **Khẳng định:** Worker hoàn toàn đáp ứng và được thiết kế chuẩn mực để chạy dưới dạng **Windows Service** gọi API.
- Đã đăng ký `AddWindowsService(options => options.ServiceName = "TiktokSyncWorkerService")` với package `Microsoft.Extensions.Hosting.WindowsServices`.
- Đã xử lý `Directory.SetCurrentDirectory(AppContext.BaseDirectory);` trong `Program.cs` để ngăn lỗi nhảy thư mục mặc định vào `C:\Windows\System32`.
- Có cơ chế ghi file log `fatal_startup_error.txt` nếu gặp lỗi crash ngay lúc khởi động.
- File cấu hình `appsettings.json` đã được dọn dẹp sạch sẽ: chỉ giữ lại `SyncJob`, `ApiSettings:BaseUrl`, và `Logging` (đã xóa bỏ các config thừa không sử dụng như DB connection string, RapidAPI key, storage paths).

### Dependencies
- `IHttpClientFactory` — gửi HTTP request tới API.
- `IConfiguration` — đọc `SyncJob:RunAtHour`, `SyncJob:RunAtMinute`, `ApiSettings:BaseUrl`.
- Package: `Microsoft.Extensions.Http`, `Serilog`, `Microsoft.Extensions.Hosting.WindowsServices`.

### Execution Flow
1. Worker chạy dưới dạng `BackgroundService` bên trong Windows Service.
2. Tính toán thời điểm chạy tiếp theo (mặc định 2:00 sáng hoặc theo config) → `Task.Delay` cho đến giờ.
3. Gọi `POST {ApiBaseUrl}/api/v1/tiktok/videos/sync` qua `HttpClient` (luôn đính kèm header `X-Trigger-Source: Worker`).
4. Parse response JSON → `BOProcessResult`, log kết quả (SUCCESS / FAILED / CRASHED).
5. Lặp lại vòng lặp cho ngày tiếp theo.

### Lệnh cài đặt Windows Service (Run as Administrator):
```cmd
sc.exe create "TiktokSyncWorkerService" binPath= "C:\path\to\publish\TitokVideos.Worker.exe" start= auto
sc.exe start "TiktokSyncWorkerService"
```

---

## Technical Points Confirmed (Rà soát & Khẳng định kỹ thuật)

| Hạng mục | Hiện trạng kỹ thuật & Khẳng định |
|----------|----------------------------------|
| **1. Client Deserialization** | - Thêm `BOProcessResult<T> : BOProcessResult` vào `TitokVideos.Shared`.<br>- .NET Client (`HomeController.Index`) deserialize trực tiếp thành `BOProcessResult<List<TiktokFeedBO>>`, không cần ép kiểu `JsonElement`.<br>- Các endpoint khác (`Sync`, `Migrate`, `Worker`) chỉ đọc `OK`, `Message` nên hoàn toàn an toàn.<br>- Client JS (`MobileSimulator.cshtml`) đọc `data.result` và `data.hasNextPage` chuẩn xác.<br>- Sửa lỗi `ViewBag.HasNext` dùng trực tiếp `result.HasNextPage` do Backend tính toán (`pageSize + 1`). |
| **2. Header `X-Trigger-Source` Compliance** | - **GUI**: Action `Sync` và `MigrateThumbnails` đều gửi `X-Trigger-Source: Manual-WebGUI` (100% tuân thủ).<br>- **Worker**: Gọi API `Sync` luôn gửi `X-Trigger-Source: Worker` (100% tuân thủ).<br>- Các request `GET` đọc feed không cần header này do không ghi nhận Download Report. |
| **3. Worker dưới dạng Windows Service** | - Đã cấu hình đầy đủ `AddWindowsService`, `Directory.SetCurrentDirectory`, Serilog rolling file.<br>- Chỉ phụ thuộc HttpClient gọi API, kiến trúc decoupled hoàn toàn không phụ thuộc DB/BLL/DAL.<br>- Config `appsettings.json` đã được dọn sạch các mục thừa. |

---
### Notes for Future Maintainers
- Khi deserialize `BOProcessResult.Result` ở .NET client, ưu tiên sử dụng generic `BOProcessResult<T>` (ví dụ: `BOProcessResult<List<TiktokFeedBO>>`) để mapping strongly-typed trực tiếp.
- `BOProcessResult.HasNextPage` là nguồn chân lý duy nhất cho phân trang Mobile/Web (được Backend tính bằng kỹ thuật `pageSize + 1`), không tự tính toán lại ở Client.
- If the storage location changes, only `ThumbnailUrlPrefix` / `VideoUrlPrefix` and **`EnsureStoragePath`** need updating.
- New media types (e.g., GIF preview) should extend `LocalMedia` and be incorporated into `DownloadMediaAsync`.
- The DAL query `GetOldThumbnailsPagedAsync` selects rows where either `ThumbnailUrl` **or** `MediaUrl` starts with `http%` so migration covers all legacy URLs, with `OFFSET/FETCH NEXT` pagination.
- **GUI** và **Worker** đều không truy cập trực tiếp BLL/DAL. Mọi nghiệp vụ đi qua **API layer** (`TitokVideos.API`).
- Khi gọi API từ client mới, hãy gửi header `X-Trigger-Source` (thêm hằng số vào `TriggerSource` + `Normalize`) để Download Report phân biệt nguồn.
- Thay đổi constructor `TiktokFeedService`: thêm `ILoggerFactory` (DI tự cung cấp).

---
*Updated on 2026‑10‑15.*
