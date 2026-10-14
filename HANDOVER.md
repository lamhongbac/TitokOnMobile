# HANDOVER Documentation

## Business Logic: `TiktokFeedService`

The **TiktokFeedService** encapsulates all business rules for synchronising TikTok video feeds, serving paginated feed data, and migrating legacy URLs to locally stored media.  The class follows a **DRY** approach – shared constants, a core download helper, and a URL‑conversion utility are used across public operations.

---
### Public API

| Method | Signature | Summary |
|--------|-----------|---------|
| **`SyncTiktokFeedsAsync`** | `public async Task<BOProcessResult> SyncTiktokFeedsAsync()` | Performs a *smart sync*: fetches the latest videos from RapidAPI, downloads **thumbnail** and **MP4** to `wwwroot/thumbnails` and `wwwroot/videos`, inserts new rows into `TiktokFeed` with **local URLs**.  If a download fails, the original TikTok URL is kept as a fallback so the feed remains usable.
| **`GetFeedsAsync`** | `public async Task<BOProcessResult> GetFeedsAsync(int pageIndex, int pageSize)` | Retrieves a paginated list of feeds.  Converts stored **local URLs** (`/thumbnails/...`, `/videos/...`) to fully qualified public URLs by prepending `ApiSettings:BaseUrl` via the shared `ToPublicUrl` helper.  Returns the result wrapped in `BOProcessResult`.
| **`MigrateOldThumbnailsAsync`** | `public async Task<BOProcessResult> MigrateOldThumbnailsAsync(int pageIndex, int pageSize)` | Scans the database (paginated) for rows whose **ThumbnailUrl** or **MediaUrl** still contain TikTok HTTP URLs, re‑fetches fresh URLs from RapidAPI, downloads missing media using the shared `DownloadMediaAsync` helper, and updates the DB fields with the new local URLs.  Pagination (`pageIndex`, `pageSize`) allows processing one page at a time from the GUI. |

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
| **`DownloadMediaAsync`** | `private async Task<LocalMedia> DownloadMediaAsync(HttpClient client, string videoId, string? coverUrl, string? playAddr)` | Core routine that attempts to download **both** thumbnail and video using the underlying `DownloadThumbnailAsync` and `DownloadVideoAsync`.  Returns a `LocalMedia` record containing the local URLs (or `null` on failure).
| **`DownloadThumbnailAsync`** | `private async Task<string?> DownloadThumbnailAsync(HttpClient client, string videoId, string sourceUrl)` | Downloads the thumbnail image, writes it to `EnsureStoragePath(..., "thumbnails")`, and returns the relative path (`/thumbnails/{videoId}.jpg`).  Returns `null` on error.
| **`DownloadVideoAsync`** | `private async Task<string?> DownloadVideoAsync(HttpClient client, string videoId, string sourceUrl)` | Streams the MP4 file to `EnsureStoragePath(..., "videos")` and returns the relative path (`/videos/{videoId}.mp4`).  Returns `null` on error.
| **`EnsureStoragePath`** | `private string EnsureStoragePath(string configKey, string defaultFolder)` | Resolves the physical folder from configuration (`ApiSettings:ThumbnailStoragePath` / `ApiSettings:VideoStoragePath`) or defaults to `wwwroot/{folder}`.  Creates the folder if it does not exist.
| **`FetchRemoteVideosAsync`** | `private async Task<List<RemoteVideo>> FetchRemoteVideosAsync(HttpClient client)` | Calls RapidAPI to obtain the latest list of videos for the configured TikTok account and maps JSON to the internal `RemoteVideo` record.
| **`LocalMedia` record** | `private sealed record LocalMedia(string? ThumbnailUrl, string? MediaUrl)` | Simple DTO used by `DownloadMediaAsync` to convey the two possible local URLs.

---
### Interaction Flow
1. **Sync** – `SyncTiktokFeedsAsync` → `FetchRemoteVideosAsync` → `DownloadMediaAsync` → `AddAsync` (DAL).
2. **Serve** – `GetFeedsAsync` reads from DAL, then `ToPublicUrl` converts stored paths to public URLs.
3. **Migrate** – `MigrateOldThumbnailsAsync(pageIndex, pageSize)` → `GetOldThumbnailsPagedAsync` (DAL) → `FetchRemoteVideosAsync` (fresh URLs) → `DownloadMediaAsync` → `UpdateThumbnailUrlAsync` / `UpdateMediaUrlAsync` (DAL).

All three public methods rely on the **same download logic** and **same URL‑prefix constants**, guaranteeing consistent behaviour across the system.

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
| **`Sync`** | POST | `POST /api/v1/tiktok/videos/sync` | Gọi API đồng bộ video mới từ TikTok. Kết quả hiển thị qua `TempData["Message"]`. |
| **`MigrateThumbnails`** | POST | `POST /api/v1/tiktok/videos/migrate-thumbnails?pageIndex={page}&pageSize={size}` | Migrate thumbnail/video cũ (URL TikTok → local) theo trang hiện tại. Kết quả hiển thị qua `TempData["Message"]`. |
| **`MobileSimulator`** | GET | — | Hiển thị trang mô phỏng Mobile WebView (iframe TikTok player). |
| **`Privacy`** | GET | — | Trang Privacy tĩnh. |
| **`Error`** | GET | — | Trang lỗi mặc định. |

### View: `Index.cshtml`
- Hai nút hành động: **"Bắt đầu Đồng bộ (Smart Sync)"** và **"Migrate old Thumbnails"**.
- Card grid hiển thị video với thumbnail, tiêu đề, lượt xem, ngày đăng.
- Phân trang (Previous / Next) dùng `ViewBag.CurrentPage` và `ViewBag.HasNext`.
- Modal popup mô phỏng Mobile WebView cho nút Review (nếu `TitokReview` = true).

---

## Worker: `TiktokSyncWorker` (API‑only architecture)

**Project:** `TitokVideos.Worker` — **không tham chiếu** tới BLL hay DAL (đã gỡ bỏ `ProjectReference`).  Chỉ phụ thuộc `TitokVideos.Shared` (cho `BOProcessResult`).

### Dependencies
- `IHttpClientFactory` — gửi HTTP request tới API.
- `IConfiguration` — đọc `SyncJob:RunAtHour`, `SyncJob:RunAtMinute`, `ApiSettings:BaseUrl`.
- Package: `Microsoft.Extensions.Http`, `Serilog`, `Microsoft.Extensions.Hosting.WindowsServices`.

### Execution Flow
1. Worker chạy dưới dạng `BackgroundService`, hỗ trợ triển khai Windows Service.
2. Tính toán thời điểm chạy tiếp theo (mặc định 2:00 sáng) → `Task.Delay` cho đến giờ.
3. Gọi `POST {ApiBaseUrl}/api/v1/tiktok/videos/sync` qua `HttpClient`.
4. Parse response JSON → `BOProcessResult`, log kết quả (SUCCESS / FAILED / CRASHED).
5. Lặp lại vòng lặp cho ngày tiếp theo.

### So sánh trước / sau refactor

| Trước | Sau |
|-------|-----|
| `IServiceProvider.CreateScope()` → `ITiktokFeedService.SyncTiktokFeedsAsync()` | `HttpClient.PostAsync("/api/v1/tiktok/videos/sync")` |
| ProjectReference: BLL + DAL + Shared | ProjectReference: **Shared only** |
| DI: `IDbConnection`, `ITiktokFeedRepository`, `ITiktokFeedService` | DI: `IHttpClientFactory` |

---
### Notes for Future Maintainers
- If the storage location changes, only `ThumbnailUrlPrefix` / `VideoUrlPrefix` and **`EnsureStoragePath`** need updating.
- New media types (e.g., GIF preview) should extend `LocalMedia` and be incorporated into `DownloadMediaAsync`.
- The DAL query `GetOldThumbnailsPagedAsync` selects rows where either `ThumbnailUrl` **or** `MediaUrl` starts with `http%` so migration covers all legacy URLs, with `OFFSET/FETCH NEXT` pagination.
- **GUI** và **Worker** đều không truy cập trực tiếp BLL/DAL. Mọi nghiệp vụ đi qua **API layer** (`TitokVideos.API`).

---
*Updated on 2026‑10‑14.*
