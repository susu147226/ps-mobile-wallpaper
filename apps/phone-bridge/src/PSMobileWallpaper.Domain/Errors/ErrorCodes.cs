namespace PSMobileWallpaper.Domain.Errors;

/// <summary>Spec §30. The only error identifiers the API may return.</summary>
public static class ErrorCodes
{
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string DeviceOffline = "DEVICE_OFFLINE";
    public const string DeviceUnauthorized = "DEVICE_UNAUTHORIZED";
    public const string AdbNotFound = "ADB_NOT_FOUND";
    public const string HdcNotFound = "HDC_NOT_FOUND";
    public const string TransportError = "TRANSPORT_ERROR";
    public const string DisplayInfoFailed = "DISPLAY_INFO_FAILED";
    public const string ImageNotFound = "IMAGE_NOT_FOUND";
    public const string ImageProcessFailed = "IMAGE_PROCESS_FAILED";
    public const string ImageUploadFailed = "IMAGE_UPLOAD_FAILED";
    public const string WallpaperNotSupported = "WALLPAPER_NOT_SUPPORTED";
    public const string WallpaperSetFailed = "WALLPAPER_SET_FAILED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string UnknownError = "UNKNOWN_ERROR";
}
