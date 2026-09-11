namespace PSMobileWallpaper.Infrastructure.Configuration;

/// <summary>Spec §24. Bound from the <c>server</c> section.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "server";

    /// <summary>Spec §23: the bridge must only ever bind loopback.</summary>
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 18765;

    /// <summary>Rejects any configured host that is not a loopback address.</summary>
    public bool IsLoopbackOnly =>
        Host is "127.0.0.1" or "localhost" or "::1" or "[::1]";
}

/// <summary>Spec §24. Bound from the <c>adb</c> section.</summary>
public sealed class AdbOptions
{
    public const string SectionName = "adb";

    public bool Enabled { get; set; } = true;

    /// <summary>Explicit path to adb.exe or its containing directory. Empty means "search PATH".</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Spec §24. Bound from the <c>hdc</c> section.</summary>
public sealed class HdcOptions
{
    public const string SectionName = "hdc";

    public bool Enabled { get; set; } = true;

    /// <summary>Explicit path to hdc.exe or its containing directory. Empty means "search PATH".</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Spec §2.5 / §24. Bound from the <c>image</c> section.</summary>
public sealed class ImageOptions
{
    public const string SectionName = "image";

    /// <summary>Spec §8: <c>png</c> or <c>jpg</c>.</summary>
    public string Format { get; set; } = "png";

    /// <summary>Spec §11: only <c>center-crop</c> is implemented in this phase.</summary>
    public string CropMode { get; set; } = "center-crop";

    public int Quality { get; set; } = 95;
}

/// <summary>Spec §20 / §24. Bound from the <c>wallpaper</c> section.</summary>
public sealed class WallpaperOptions
{
    public const string SectionName = "wallpaper";

    public bool SaveToGallery { get; set; } = true;

    public bool SetLock { get; set; } = true;
}
