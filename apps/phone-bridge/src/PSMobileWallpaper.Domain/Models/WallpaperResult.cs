namespace PSMobileWallpaper.Domain.Models;

/// <summary>Spec §29.</summary>
public sealed class WallpaperResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? DeviceId { get; set; }

    public static WallpaperResult Ok(string deviceId, string message = "") => new()
    {
        Success = true,
        DeviceId = deviceId,
        Message = message,
    };

    public static WallpaperResult Fail(string deviceId, string errorCode, string message) => new()
    {
        Success = false,
        DeviceId = deviceId,
        ErrorCode = errorCode,
        Message = message,
    };
}
