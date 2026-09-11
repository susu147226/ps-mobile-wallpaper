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

    /// <summary>
    /// Determines capabilities for a specific device. This is asynchronous and takes the transport
    /// because a provider may need to inspect the device (for example, to see whether its companion
    /// helper app is installed) before it can honestly claim what it supports.
    /// </summary>
    Task<WallpaperCapabilities> GetCapabilitiesAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetLockAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetHomeAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);

    Task<WallpaperResult> SetBothAsync(DeviceInfo device, IDeviceTransport transport, string imagePath, CancellationToken cancellationToken = default);
}
