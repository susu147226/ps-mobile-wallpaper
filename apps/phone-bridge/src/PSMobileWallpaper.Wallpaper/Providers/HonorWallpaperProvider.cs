using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>Spec §18. Honor (Magic/Honor series).</summary>
public sealed class HonorWallpaperProvider : BrandWallpaperProviderBase
{
    public HonorWallpaperProvider(ILogger<HonorWallpaperProvider> logger)
        : base(logger, ["honor"]) { }

    public override string Name => "HonorWallpaperProvider";
}
