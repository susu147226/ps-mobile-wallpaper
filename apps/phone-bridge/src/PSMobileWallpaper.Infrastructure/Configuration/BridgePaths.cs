namespace PSMobileWallpaper.Infrastructure.Configuration;

/// <summary>
/// Spec §2.6 / §2.7 / §23. The single definition of where the bridge keeps its state:
/// %AppData%/PSMobileWallpaper/
/// </summary>
public static class BridgePaths
{
    public const string ApplicationFolderName = "PSMobileWallpaper";

    /// <summary>%AppData%/PSMobileWallpaper</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationFolderName);

    /// <summary>Spec §2.6: %AppData%/PSMobileWallpaper/logs/</summary>
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    /// <summary>Spec §2.7: %AppData%/PSMobileWallpaper/config.json</summary>
    public static string ConfigFilePath { get; } = Path.Combine(RootDirectory, "config.json");

    /// <summary>Spec §23: the local authentication token handed to the Photoshop plugin.</summary>
    public static string TokenFilePath { get; } = Path.Combine(RootDirectory, "auth.token");

    /// <summary>Spec §8: %TEMP%/PSMobileWallpaper/ for exported and processed images.</summary>
    public static string TempWorkspaceDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        ApplicationFolderName);

    /// <summary>
    /// The Android helper APK shipped next to the bridge. It is installed on demand when a device
    /// needs a wallpaper set through the official WallpaperManager API (see the helper's README).
    /// </summary>
    public static string HelperApkPath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "helpers",
        "psmw-wallpaper-helper.apk");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(TempWorkspaceDirectory);
    }
}
