using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PSMobileWallpaper.Infrastructure.Configuration;

/// <summary>
/// Spec §2.7 / §24. Reads config.json from the AppData folder and seeds it with the documented
/// defaults on first run, so a fresh install always has an editable file.
/// </summary>
public static class BridgeConfiguration
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IConfigurationRoot Load()
    {
        BridgePaths.EnsureCreated();
        WriteDefaultIfMissing();

        return new ConfigurationBuilder()
            .SetBasePath(BridgePaths.RootDirectory)
            .AddJsonFile("config.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables("PSMW_")
            .Build();
    }

    /// <summary>Writes the spec §24 example shape when no config exists yet.</summary>
    public static void WriteDefaultIfMissing()
    {
        if (File.Exists(BridgePaths.ConfigFilePath))
        {
            return;
        }

        var defaults = new
        {
            server = new { host = "127.0.0.1", port = 18765, requireToken = false },
            adb = new { enabled = true, path = string.Empty },
            hdc = new { enabled = true, path = string.Empty },
            image = new { format = "png", cropMode = "center-crop", quality = 95 },
            wallpaper = new { saveToGallery = true, setLock = true },
        };

        File.WriteAllText(
            BridgePaths.ConfigFilePath,
            JsonSerializer.Serialize(defaults, WriteOptions));
    }
}
