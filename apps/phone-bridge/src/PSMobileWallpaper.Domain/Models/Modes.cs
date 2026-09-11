namespace PSMobileWallpaper.Domain.Models;

/// <summary>
/// Spec §11. Only <see cref="CenterCrop"/> is implemented in the current phase;
/// the remaining members are declared so the wire format is stable when they land.
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
