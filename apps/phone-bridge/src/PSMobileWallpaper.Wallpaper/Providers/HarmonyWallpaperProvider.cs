using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Spec §18. HarmonyOS over HDC. Uses the HarmonyOS media library layout rather than Android's
/// <c>/sdcard/Pictures</c>. Lock-screen assignment is not claimed (spec §40).
/// </summary>
public sealed class HarmonyWallpaperProvider : WallpaperProviderBase
{
    public HarmonyWallpaperProvider(ILogger<HarmonyWallpaperProvider> logger) : base(logger) { }

    public override string Name => "HarmonyWallpaperProvider";

    protected override string GalleryDirectory => "/storage/media/100/local/files/Pictures/PSMobileWallpaper";

    public override bool CanHandle(DeviceInfo device) => device.Transport == Domain.Models.DeviceTransport.Hdc;

    /// <summary>HarmonyOS has no MEDIA_SCANNER broadcast; the media library indexes the directory itself.</summary>
    protected override Task TriggerMediaScanAsync(
        DeviceInfo device,
        Transport.Abstractions.IDeviceTransport transport,
        string remotePath,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
