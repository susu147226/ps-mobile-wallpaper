using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>Spec §26. vivo and its iQOO sub-brand.</summary>
public sealed class VivoDeviceAdapter : AndroidDeviceAdapter
{
    private static readonly string[] VivoAliases = ["vivo", "iqoo"];

    public VivoDeviceAdapter() : base(VivoAliases) { }

    public override string Name => "VivoDeviceAdapter";

    public override bool CanHandle(Abstractions.DeviceProbe probe) =>
        base.CanHandle(probe) &&
        (MatchesAlias(probe.Get("ro.product.brand")) || MatchesAlias(probe.Get("ro.product.manufacturer")));

    protected override void ApplyBrandDefaults(Domain.Models.DeviceInfo device)
    {
        var isIqoo = device.Brand.Contains("iqoo", StringComparison.OrdinalIgnoreCase);

        device.Brand = isIqoo ? "iQOO" : "vivo";
        device.Manufacturer = "vivo";
    }
}
