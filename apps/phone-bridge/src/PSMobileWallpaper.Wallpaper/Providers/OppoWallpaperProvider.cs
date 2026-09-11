using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. OPPO / realme (ColorOS).</summary>
public sealed class OppoWallpaperProvider : BrandWallpaperProviderBase
{
    public OppoWallpaperProvider(ILogger<OppoWallpaperProvider> logger)
        : base(logger, ["oppo", "realme"]) { }

    public override string Name => "OppoWallpaperProvider";
}
