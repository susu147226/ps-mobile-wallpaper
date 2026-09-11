using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Image.Abstractions;

/// <summary>Spec §12. The only way the rest of the bridge touches pixels.</summary>
public interface IImageProcessor
{
    Task<ImageInfo> GetInfoAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Center-crops to the given target size and returns the path of the written file.</summary>
    Task<string> CenterCropAsync(string imagePath, int width, int height, CancellationToken cancellationToken = default);

    /// <summary>Resizes to the given target size and returns the path of the written file.</summary>
    Task<string> ResizeAsync(string imagePath, int width, int height, CancellationToken cancellationToken = default);
}
