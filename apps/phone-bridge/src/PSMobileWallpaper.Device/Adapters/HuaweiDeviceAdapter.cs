using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>Spec §26. Huawei handsets running EMUI/Android over ADB.</summary>
public sealed class HuaweiDeviceAdapter : AndroidDeviceAdapter
{
    private static readonly string[] HuaweiAliases = ["huawei"];

    public HuaweiDeviceAdapter() : base(HuaweiAliases) { }

    public override string Name => "HuaweiDeviceAdapter";

    public override bool CanHandle(Abstractions.DeviceProbe probe) =>
        base.CanHandle(probe) &&
        (MatchesAlias(probe.Get("ro.product.brand")) || MatchesAlias(probe.Get("ro.product.manufacturer")));

    protected override void ApplyBrandDefaults(Domain.Models.DeviceInfo device)
    {
        device.Brand = "Huawei";
        device.Manufacturer = "HUAWEI";
    }
}
