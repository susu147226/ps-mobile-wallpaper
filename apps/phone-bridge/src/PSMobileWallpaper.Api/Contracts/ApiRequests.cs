using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Image;

namespace PSMobileWallpaper.Api.Contracts;

/// <summary>Body for <c>POST /api/v1/images</c>. The plugin and the bridge share a filesystem, so paths are used instead of large uploads.</summary>
public sealed record ImageRequest(string Path);

/// <summary>Source region for <see cref="CropMode.Custom"/>, in source pixels.</summary>
public sealed record CropRegionRequest(double X, double Y, double Width, double Height);

/// <summary>Body for <c>POST /api/v1/images/crop</c> (spec §10 / §11).</summary>
public sealed record CropRequest(
    string DeviceId,
    string Path,
    int? Width = null,
    int? Height = null,
    string? Mode = null,
    CropRegionRequest? Region = null);

/// <summary>Body for <c>POST /api/v1/wallpaper/prepare</c> (spec §10 / §11).</summary>
public sealed record PrepareWallpaperRequest(
    string DeviceId,
    string Path,
    int? Width = null,
    int? Height = null,
    string? Mode = null,
    CropRegionRequest? Region = null);

/// <summary>Body for <c>POST /api/v1/wallpaper/send</c>.</summary>
public sealed record SendWallpaperRequest(string DeviceId, string ImagePath);

/// <summary>Body for the <c>POST /api/v1/wallpaper/set-*</c> endpoints.</summary>
public sealed record SetWallpaperRequest(string DeviceId, string ImagePath, WallpaperMode Mode = WallpaperMode.SaveToGalleryAndSetLock);

internal static class CropRequestExtensions
{
    /// <summary>Resolves the requested mode, falling back to the configured/default center-crop.</summary>
    public static CropMode ResolveMode(string? mode, CropMode configuredDefault) =>
        string.IsNullOrWhiteSpace(mode) ? configuredDefault : CropModes.Parse(mode);

    public static CropRect? ToCropRect(this CropRegionRequest? region) =>
        region is null ? null : new CropRect(region.X, region.Y, region.Width, region.Height);
}
