namespace PSMobileWallpaper.Domain.Models;

/// <summary>Spec §19. Gates which wallpaper operations a device actually supports.</summary>
public sealed class WallpaperCapabilities
{
    public bool CanSetLock { get; set; }
    public bool CanSetHome { get; set; }
    public bool CanSetBoth { get; set; }
    public bool CanSaveToGallery { get; set; }
    public bool RequiresUserConfirmation { get; set; }

    /// <summary>Conservative default: gallery only, nothing is claimed until a provider verifies it (spec §40).</summary>
    public static WallpaperCapabilities None { get; } = new()
    {
        CanSetLock = false,
        CanSetHome = false,
        CanSetBoth = false,
        CanSaveToGallery = false,
        RequiresUserConfirmation = true,
    };
}
