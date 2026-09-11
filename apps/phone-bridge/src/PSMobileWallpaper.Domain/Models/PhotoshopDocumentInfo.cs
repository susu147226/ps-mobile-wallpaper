namespace PSMobileWallpaper.Domain.Models;

/// <summary>Spec §7. Snapshot of the active Photoshop document.</summary>
public sealed class PhotoshopDocumentInfo
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Resolution { get; set; }
    public string ColorMode { get; set; } = string.Empty;

    public double AspectRatio => Height == 0 ? 0d : (double)Width / Height;
}
