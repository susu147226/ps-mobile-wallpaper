using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>Spec §26. Honor devices (Magic/Honor series), which report their own brand since the split from Huawei.</summary>
public sealed class HonorDeviceAdapter : AndroidDeviceAdapter
{
    private static readonly string[] HonorAliases = ["honor"];

    public HonorDeviceAdapter() : base(HonorAliases) { }

    public override string Name => "HonorDeviceAdapter";

    public override bool CanHandle(Abstractions.DeviceProbe probe) =>
        base.CanHandle(probe) &&
        (MatchesAlias(probe.Get("ro.product.brand")) || MatchesAlias(probe.Get("ro.product.manufacturer")));

    protected override void ApplyBrandDefaults(Domain.Models.DeviceInfo device)
    {
        device.Brand = "Honor";
        device.Manufacturer = "HONOR";
    }
}
