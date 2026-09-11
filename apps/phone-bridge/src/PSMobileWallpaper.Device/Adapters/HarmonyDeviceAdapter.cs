using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Adapters;

/// <summary>
/// Spec §26. HarmonyOS devices reached over HDC. Uses the `param get` key space
/// rather than Android's `getprop` keys.
/// </summary>
public sealed class HarmonyDeviceAdapter : Abstractions.IDeviceAdapter
{
    public string Name => "HarmonyDeviceAdapter";

    public bool CanHandle(Abstractions.DeviceProbe probe) =>
        probe.Device.Transport == DeviceTransport.Hdc;

    public void Enrich(Abstractions.DeviceProbe probe)
    {
        var device = probe.Device;

        device.Brand = probe.Get("const.product.brand");
        device.Manufacturer = probe.Get("const.product.manufacturer");
        device.Model = probe.Get("const.product.model");

        if (string.IsNullOrWhiteSpace(device.Brand))
        {
            device.Brand = string.IsNullOrWhiteSpace(device.Manufacturer) ? "HarmonyOS" : device.Manufacturer;
        }

        if (string.IsNullOrWhiteSpace(device.Manufacturer))
        {
            device.Manufacturer = device.Brand;
        }

        device.Os = string.IsNullOrWhiteSpace(probe.OsName) ? "HarmonyOS" : probe.OsName;
        device.OsVersion = string.IsNullOrWhiteSpace(probe.OsVersion)
            ? probe.Get("const.product.software.version")
            : probe.OsVersion;
    }
}
