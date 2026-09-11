using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Abstractions;

/// <summary>Spec §17. The only entry point for changing what the phone displays.</summary>
public interface IWallpaperService
{
    Task<WallpaperCapabilities> GetCapabilitiesAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the image into the phone's gallery and nothing else.
    ///
    /// This is the one operation that works on every device this project supports, including the
    /// ones whose lock screen cannot be set by an app. It is separate from the set-* methods so
    /// succeeding here is never reported as a failure because the wallpaper step was refused.
    /// </summary>
    Task<WallpaperResult> SaveToGalleryAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetLockWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetHomeWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetBothWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);
}
