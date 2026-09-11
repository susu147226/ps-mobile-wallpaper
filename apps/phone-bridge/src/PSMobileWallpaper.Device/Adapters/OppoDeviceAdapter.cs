using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>Spec §26. OPPO and its realme sibling report the same OEM property set.</summary>
public sealed class OppoDeviceAdapter : AndroidDeviceAdapter
{
    private static readonly string[] OppoAliases = ["oppo", "realme"];

    public OppoDeviceAdapter() : base(OppoAliases) { }

    public override string Name => "OppoDeviceAdapter";

    public override bool CanHandle(Abstractions.DeviceProbe probe) =>
        base.CanHandle(probe) &&
        (MatchesAlias(probe.Get("ro.product.brand")) || MatchesAlias(probe.Get("ro.product.manufacturer")));

    protected override void ApplyBrandDefaults(Domain.Models.DeviceInfo device)
    {
        var isRealme = device.Brand.Contains("realme", StringComparison.OrdinalIgnoreCase);

        device.Brand = isRealme ? "realme" : "OPPO";
        device.Manufacturer = isRealme ? "realme" : "OPPO";
    }
}
