using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. Huawei on Android/EMUI. Lock-screen assignment not yet verified on hardware.</summary>
public sealed class HuaweiWallpaperProvider : BrandWallpaperProviderBase
{
    public HuaweiWallpaperProvider(ILogger<HuaweiWallpaperProvider> logger)
        : base(logger, ["huawei"]) { }

    public override string Name => "HuaweiWallpaperProvider";
}
