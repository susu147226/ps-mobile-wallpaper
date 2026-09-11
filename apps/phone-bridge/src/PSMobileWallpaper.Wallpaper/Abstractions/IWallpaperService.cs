using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Abstractions;

/// <summary>Spec §17. The only entry point for changing what the phone displays.</summary>
public interface IWallpaperService
{
    Task<WallpaperCapabilities> GetCapabilitiesAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetLockWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetHomeWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetBothWallpaperAsync(DeviceInfo device, string imagePath, CancellationToken cancellationToken = default);
}
