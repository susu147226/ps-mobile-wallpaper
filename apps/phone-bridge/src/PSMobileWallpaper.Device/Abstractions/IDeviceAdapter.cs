using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Abstractions;

/// <summary>
/// Normalized property bag read from a device, regardless of transport.
/// Android fills this from <c>getprop</c>; HarmonyOS from <c>param get</c>.
/// </summary>
public sealed class DeviceProbe
{
    public required DeviceInfo Device { get; init; }

    public required IReadOnlyDictionary<string, string> Properties { get; init; }

    public string OsName { get; init; } = string.Empty;

    public string OsVersion { get; init; } = string.Empty;

    public string Get(string key) =>
        Properties.TryGetValue(key, out var value) ? value : string.Empty;
}

/// <summary>
/// Spec §26. One adapter per brand family. Adapters only add identity information —
/// they never set wallpaper capabilities, which are the Wallpaper layer's decision (spec §18/§19).
/// </summary>
public interface IDeviceAdapter
{
    /// <summary>Adapter name; matches the spec §26 naming.</summary>
    string Name { get; }

    /// <summary>True when this adapter owns the probed device. Exactly one adapter should claim it.</summary>
    bool CanHandle(DeviceProbe probe);

    /// <summary>Fills in brand, manufacturer, model and OS fields on the probe's device.</summary>
    void Enrich(DeviceProbe probe);
}
