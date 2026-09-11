using Microsoft.Extensions.Logging;

namespace PSMobileWallpaper.Application;

/// <summary>
/// Remembers the most recently prepared wallpaper so a phone-side helper app can download it.
///
/// This exists because neither tested platform lets the bridge put a file into the gallery itself:
/// HarmonyOS's media library cannot be written over HDC, and its wallpaper API is a stub. The app
/// fetches the bytes over loopback (see `hdc rport`) and the user saves them from there.
/// </summary>
public sealed class PreparedImageStore
{
    private readonly ILogger<PreparedImageStore> _logger;
    private readonly Lock _gate = new();

    private string? _path;

    public PreparedImageStore(ILogger<PreparedImageStore> logger) => _logger = logger;

    public void Set(string path)
    {
        lock (_gate)
        {
            _path = path;
        }

        _logger.LogInformation("Prepared image is now available for download: '{Path}'.", path);
    }

    /// <summary>Returns the current image path, or null when nothing has been prepared yet.</summary>
    public string? Get()
    {
        lock (_gate)
        {
            return _path is not null && File.Exists(_path) ? _path : null;
        }
    }
}
