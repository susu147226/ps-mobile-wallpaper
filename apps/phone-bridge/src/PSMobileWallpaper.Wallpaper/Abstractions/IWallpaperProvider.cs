using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Wallpaper.Abstractions;

/// <summary>
/// Spec §18. One provider per brand family. A provider decides <see cref="WallpaperCapabilities"/>
/// for the devices it owns; anything it cannot verify must be reported as unsupported (spec §40).
/// </summary>
public interface IWallpaperProvider
{
    string Name { get; }

    bool CanHandle(DeviceInfo device);

    WallpaperCapabilities GetCapabilities(DeviceInfo device);

    Task<WallpaperResult> SetLockAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetHomeAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetBothAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);
}
