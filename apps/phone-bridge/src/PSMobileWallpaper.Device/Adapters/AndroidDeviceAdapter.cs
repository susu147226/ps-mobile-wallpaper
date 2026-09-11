using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>
/// Base adapter for anything reachable over ADB. Maps the standard Android property set
/// (spec §5.4) and acts as the fallback for brands without a dedicated adapter.
/// </summary>
public class AndroidDeviceAdapter : Abstractions.IDeviceAdapter
{
    private static readonly string[] BrandAliases = [];

    private readonly string[] _aliases;

    public AndroidDeviceAdapter() : this(BrandAliases) { }

    protected AndroidDeviceAdapter(string[] aliases) => _aliases = aliases;

    public virtual string Name => "AndroidDeviceAdapter";

    public virtual bool CanHandle(Abstractions.DeviceProbe probe) =>
        probe.Device.Transport == DeviceTransport.Adb;

    public virtual void Enrich(Abstractions.DeviceProbe probe)
    {
        var device = probe.Device;

        device.Brand = Normalize(probe.Get("ro.product.brand"));
        device.Manufacturer = Normalize(probe.Get("ro.product.manufacturer"));
        device.Model = probe.Get("ro.product.model");
        device.Os = string.IsNullOrWhiteSpace(probe.OsName) ? "Android" : probe.OsName;
        device.OsVersion = string.IsNullOrWhiteSpace(probe.OsVersion)
            ? probe.Get("ro.build.version.release")
            : probe.OsVersion;

        ApplyBrandDefaults(device);
    }

    /// <summary>Hook for brand adapters that need to reshape identity (e.g. fold `Redmi` into `Xiaomi`).</summary>
    protected virtual void ApplyBrandDefaults(Domain.Models.DeviceInfo device) { }

    protected bool MatchesAlias(string value) =>
        _aliases.Length > 0 && _aliases.Any(alias => value.Contains(alias, StringComparison.OrdinalIgnoreCase));

    /// <summary>Device props are inconsistently cased across vendors; title-case single-token values for display.</summary>
    protected static string Normalize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return trimmed;
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
