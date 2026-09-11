using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. OPPO / realme (ColorOS).</summary>
public sealed class OppoWallpaperProvider : BrandWallpaperProviderBase
{
    public OppoWallpaperProvider(ILogger<OppoWallpaperProvider> logger, string helperApkPath)
        : base(logger, ["oppo", "realme"], helperApkPath) { }

    public override string Name => "OppoWallpaperProvider";
}
