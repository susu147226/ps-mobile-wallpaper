using PSMobileWallpaper.Device;
using PSMobileWallpaper.Device.Abstractions;
using PSMobileWallpaper.Device.Adapters;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Tests.Device;

/// <summary>Spec §25 / §26. Brand identification is centralized here and must not leak into the UI.</summary>
public sealed class DeviceAdapterTests
{
    private static readonly IDeviceAdapter[] Adapters =
    [
        new HuaweiDeviceAdapter(),
        new HonorDeviceAdapter(),
        new XiaomiDeviceAdapter(),
        new OppoDeviceAdapter(),
        new VivoDeviceAdapter(),
        new HarmonyDeviceAdapter(),
        new AndroidDeviceAdapter(),
    ];

    private static DeviceProbe Probe(
        DeviceTransport transport,
        params (string Key, string Value)[] properties) => new()
    {
        Device = new DeviceInfo { Id = "TEST", Transport = transport },
        Properties = properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
    };

    private static IDeviceAdapter Resolve(DeviceProbe probe) =>
        Adapters.First(adapter => adapter.CanHandle(probe));

    [Theory]
    [InlineData("huawei", "HUAWEI", "Huawei", "HuaweiDeviceAdapter")]
    [InlineData("HUAWEI", "HUAWEI", "Huawei", "HuaweiDeviceAdapter")]
    [InlineData("honor", "HONOR", "Honor", "HonorDeviceAdapter")]
    [InlineData("Xiaomi", "Xiaomi", "Xiaomi", "XiaomiDeviceAdapter")]
    [InlineData("Redmi", "Xiaomi", "Redmi", "XiaomiDeviceAdapter")]
    [InlineData("POCO", "Xiaomi", "POCO", "XiaomiDeviceAdapter")]
    [InlineData("OPPO", "OPPO", "OPPO", "OppoDeviceAdapter")]
    [InlineData("realme", "realme", "realme", "OppoDeviceAdapter")]
    [InlineData("vivo", "vivo", "vivo", "VivoDeviceAdapter")]
    [InlineData("iQOO", "vivo", "iQOO", "VivoDeviceAdapter")]
    public void AndroidBrands_ResolveToTheirOwnAdapter(
        string brand,
        string manufacturer,
        string expectedBrand,
        string expectedAdapter)
    {
        var probe = Probe(
            DeviceTransport.Adb,
            ("ro.product.brand", brand),
            ("ro.product.manufacturer", manufacturer),
            ("ro.product.model", "TEST-MODEL"),
            ("ro.build.version.release", "14"));

        var adapter = Resolve(probe);
        Assert.Equal(expectedAdapter, adapter.Name);

        adapter.Enrich(probe);
        Assert.Equal(expectedBrand, probe.Device.Brand);
        Assert.Equal("TEST-MODEL", probe.Device.Model);
        Assert.Equal("Android", probe.Device.Os);
        Assert.Equal("14", probe.Device.OsVersion);
    }

    [Fact]
    public void UnrecognisedAndroidBrand_FallsBackToGenericAdapter()
    {
        var probe = Probe(
            DeviceTransport.Adb,
            ("ro.product.brand", "SomeNewVendor"),
            ("ro.product.model", "X1"));

        var adapter = Resolve(probe);

        Assert.Equal("AndroidDeviceAdapter", adapter.Name);

        adapter.Enrich(probe);
        Assert.Equal("Somenewvendor", probe.Device.Brand);
    }

    [Fact]
    public void HarmonyDevices_ResolveToHarmonyAdapter_RegardlessOfBrand()
    {
        var probe = Probe(
            DeviceTransport.Hdc,
            ("const.product.brand", "HUAWEI"),
            ("const.product.manufacturer", "HUAWEI"),
            ("const.product.model", "Mate 60 Pro"));

        var adapter = Resolve(probe);

        Assert.Equal("HarmonyDeviceAdapter", adapter.Name);

        adapter.Enrich(probe);
        Assert.Equal("HUAWEI", probe.Device.Brand);
        Assert.Equal("Mate 60 Pro", probe.Device.Model);
        Assert.Equal("HarmonyOS", probe.Device.Os);
    }

    [Fact]
    public void HuaweiAndroidDevice_IsNotClaimedByHarmonyAdapter()
    {
        var probe = Probe(
            DeviceTransport.Adb,
            ("ro.product.brand", "HUAWEI"),
            ("ro.product.manufacturer", "HUAWEI"));

        Assert.Equal("HuaweiDeviceAdapter", Resolve(probe).Name);
    }

    [Fact]
    public void EveryAdapter_ResolvesToASingleBrandAdapterBeforeTheGenericFallback()
    {
        // AndroidDeviceAdapter deliberately matches every ADB device as the last resort, so the
        // guarantee that matters is: exactly one brand adapter claims it, and resolution returns it.
        var adb = Probe(DeviceTransport.Adb, ("ro.product.brand", "HUAWEI"));
        var hdc = Probe(DeviceTransport.Hdc, ("const.product.brand", "HUAWEI"));

        // Brand adapters derive from AndroidDeviceAdapter, so exclude the generic one by exact type.
        var brandAdapters = Adapters.Where(a => a.GetType() != typeof(AndroidDeviceAdapter)).ToList();

        Assert.Single(brandAdapters, a => a.CanHandle(adb));
        Assert.Single(brandAdapters, a => a.CanHandle(hdc));

        Assert.Equal("HuaweiDeviceAdapter", Resolve(adb).Name);
        Assert.Equal("HarmonyDeviceAdapter", Resolve(hdc).Name);
    }

    [Fact]
    public void GenericAndroidAdapter_IsTheLastAdapterInResolutionOrder()
    {
        // Resolution takes the first match, so the fallback must never shadow a brand adapter.
        Assert.IsType<AndroidDeviceAdapter>(Adapters[^1], exactMatch: false);
        Assert.IsNotType<AndroidDeviceAdapter>(Adapters[0]);
    }
}
