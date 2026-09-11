namespace PSMobileWallpaper.Domain.Models;

using System.Text.Json.Serialization;

/// <summary>Spec §28.</summary>
public sealed class DisplayInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Density { get; set; }
    public int Rotation { get; set; }
    public ScreenOrientation Orientation { get; set; }

    /// <summary>Pixel count of the current orientation. Used for ratio math and progress sizing.</summary>
    [JsonIgnore]
    public long PixelCount => (long)Width * Height;
}
