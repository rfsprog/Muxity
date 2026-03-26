namespace Muxity.Web.Models;

// ── Auth ──────────────────────────────────────────────────────────────────────
public record CallbackRequest(string Provider, string IdToken);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserInfo User);

public record UserInfo(
    string Id,
    string Email,
    string DisplayName,
    string Provider);

// ── Video ─────────────────────────────────────────────────────────────────────
public record VideoSummary(
    string Id,
    string Title,
    string Description,
    string Status,
    string Visibility,
    string? ThumbnailPath,
    double? DurationSeconds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record VideoDetail(
    string Id,
    string OwnerId,
    string Title,
    string Description,
    string Status,
    string Visibility,
    string? ThumbnailPath,
    double? DurationSeconds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PagedResponse<T>(
    List<T> Items,
    int Page,
    int PageSize,
    long TotalCount);

public record UploadStatusResponse(
    string VideoId,
    string Status,
    int? Progress,
    string? Error);

public record StreamingKeyResponse(
    string VideoId,
    string Key,
    DateTime? ExpiresAt);
