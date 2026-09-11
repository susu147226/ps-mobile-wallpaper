using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. vivo / iQOO (OriginOS, Funtouch OS).</summary>
public sealed class VivoWallpaperProvider : BrandWallpaperProviderBase
{
    public VivoWallpaperProvider(ILogger<VivoWallpaperProvider> logger, string helperApkPath)
        : base(logger, ["vivo", "iqoo"], helperApkPath) { }

    public override string Name => "VivoWallpaperProvider";
}
