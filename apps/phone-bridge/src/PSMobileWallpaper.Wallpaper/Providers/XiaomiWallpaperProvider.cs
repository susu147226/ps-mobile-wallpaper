using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. Xiaomi / Redmi / POCO (MIUI, HyperOS).</summary>
public sealed class XiaomiWallpaperProvider : BrandWallpaperProviderBase
{
    public XiaomiWallpaperProvider(ILogger<XiaomiWallpaperProvider> logger)
        : base(logger, ["xiaomi", "redmi", "poco"]) { }

    public override string Name => "XiaomiWallpaperProvider";
}
