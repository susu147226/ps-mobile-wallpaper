using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Api.Contracts;

/// <summary>Body for <c>POST /api/v1/images</c>. The plugin and the bridge share a filesystem, so paths are used instead of large uploads.</summary>
public sealed record ImageRequest(string Path);

/// <summary>Body for <c>POST /api/v1/images/crop</c> (spec §10).</summary>
public sealed record CropRequest(string DeviceId, string Path, int? Width = null, int? Height = null);

/// <summary>Body for <c>POST /api/v1/wallpaper/prepare</c>.</summary>
public sealed record PrepareWallpaperRequest(string DeviceId, string Path, int? Width = null, int? Height = null);

/// <summary>Body for <c>POST /api/v1/wallpaper/send</c>.</summary>
public sealed record SendWallpaperRequest(string DeviceId, string ImagePath);

/// <summary>Body for the <c>POST /api/v1/wallpaper/set-*</c> endpoints.</summary>
public sealed record SetWallpaperRequest(string DeviceId, string ImagePath, WallpaperMode Mode = WallpaperMode.SaveToGalleryAndSetLock);
