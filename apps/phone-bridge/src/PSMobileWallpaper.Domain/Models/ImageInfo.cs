namespace PSMobileWallpaper.Domain.Models;

/// <summary>Metadata for an image file on disk. Referenced by IImageProcessor (spec §12).</summary>
public sealed class ImageInfo
{
    public string Path { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public double AspectRatio => Height == 0 ? 0d : (double)Width / Height;
}
