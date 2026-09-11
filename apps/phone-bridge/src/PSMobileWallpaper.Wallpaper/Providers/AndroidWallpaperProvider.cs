using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. Generic Android fallback: gallery only, no verified lock/home support.</summary>
public sealed class AndroidWallpaperProvider : WallpaperProviderBase
{
    public AndroidWallpaperProvider(ILogger<AndroidWallpaperProvider> logger) : base(logger) { }

    public override string Name => "AndroidWallpaperProvider";

    public override bool CanHandle(DeviceInfo device) => device.Transport == Domain.Models.DeviceTransport.Adb;
}
