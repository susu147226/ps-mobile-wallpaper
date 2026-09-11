using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Spec §18. Generic Android fallback. Sets wallpapers through the bundled helper app when it is
/// installed; otherwise it only offers gallery save.
/// </summary>
public sealed class AndroidWallpaperProvider : AndroidHelperWallpaperProviderBase
{
    public AndroidWallpaperProvider(ILogger<AndroidWallpaperProvider> logger, string helperApkPath)
        : base(logger, helperApkPath) { }

    public override string Name => "AndroidWallpaperProvider";

    public override bool CanHandle(DeviceInfo device) => device.Transport == Domain.Models.DeviceTransport.Adb;
}
