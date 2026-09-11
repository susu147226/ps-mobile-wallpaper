using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>
/// Spec §26 / §25. Covers the Xiaomi family. Redmi and POCO are separate brands in spec §25,
/// so the advertised brand is preserved rather than folded into "Xiaomi".
/// </summary>
public sealed class XiaomiDeviceAdapter : AndroidDeviceAdapter
{
    private static readonly string[] XiaomiAliases = ["xiaomi", "redmi", "poco"];

    public XiaomiDeviceAdapter() : base(XiaomiAliases) { }

    public override string Name => "XiaomiDeviceAdapter";

    public override bool CanHandle(Abstractions.DeviceProbe probe) =>
        base.CanHandle(probe) &&
        (MatchesAlias(probe.Get("ro.product.brand")) || MatchesAlias(probe.Get("ro.product.manufacturer")));

    protected override void ApplyBrandDefaults(Domain.Models.DeviceInfo device)
    {
        device.Manufacturer = "Xiaomi";
        device.Brand = CanonicalBrand(device.Brand);
    }

    /// <summary>Restores the vendor's own casing for the sub-brands in spec §25.</summary>
    private static string CanonicalBrand(string detected) => detected.ToLowerInvariant() switch
    {
        "redmi" => "Redmi",
        "poco" => "POCO",
        _ => "Xiaomi",
    };
}
