namespace PSMobileWallpaper.Domain.Models;

/// <summary>
/// Spec §11. <see cref="CenterCrop"/> is the default; the rest are opt-in per the config or request.
/// </summary>
public enum CropMode
{
    CenterCrop = 0,
    CenterFit = 1,
    Stretch = 2,
    TopCrop = 3,
    BottomCrop = 4,
    Custom = 5,
}

/// <summary>Maps the kebab-case crop names used in config.json / the API to <see cref="CropMode"/>.</summary>
public static class CropModes
{
    private static readonly Dictionary<string, CropMode> ByConfigName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["center-crop"] = CropMode.CenterCrop,
        ["center-fit"] = CropMode.CenterFit,
        ["stretch"] = CropMode.Stretch,
        ["top-crop"] = CropMode.TopCrop,
        ["bottom-crop"] = CropMode.BottomCrop,
        ["custom"] = CropMode.Custom,
    };

    /// <summary>Falls back to <see cref="CropMode.CenterCrop"/> for null, empty or unrecognised values.</summary>
    public static CropMode Parse(string? value) =>
        value is not null && ByConfigName.TryGetValue(value, out var mode) ? mode : CropMode.CenterCrop;

    /// <summary>The config-facing name, e.g. <c>center-crop</c>.</summary>
    public static string ToConfigName(CropMode mode) =>
        ByConfigName.FirstOrDefault(pair => pair.Value == mode).Key ?? "center-crop";
}

/// <summary>Spec §2.5 / §8. Formats the bridge can encode.</summary>
public enum ImageFormat
{
    Png = 0,
    Jpeg = 1,
}

/// <summary>Spec §20. Default is <see cref="SaveToGalleryAndSetLock"/>.</summary>
public enum WallpaperMode
{
    SetLockOnly = 0,
    SaveToGalleryAndSetLock = 1,
    SaveToGalleryOnly = 2,
}
