using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Adb;

namespace PSMobileWallpaper.IntegrationTests;

/// <summary>
/// Spec §34 Phase 3. Verifies the ADB transport against a real `adb` binary, and against a real
/// handset when one is attached. Tests requiring hardware skip themselves otherwise.
/// </summary>
[Collection("transports")]
public sealed class AdbTransportIntegrationTests
{
    private readonly TransportFixture _fixture;

    public AdbTransportIntegrationTests(TransportFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task IsAvailable_WheneverAdbIsInstalled()
    {
        Skip.IfNot(File.Exists(_fixture.Adb.ExecutablePath), $"adb not found at '{_fixture.Adb.ExecutablePath}'.");

        Assert.True(await _fixture.Adb.IsAvailableAsync());
    }

    [SkippableFact]
    public void KindAndExecutablePath_AreAdb()
    {
        Assert.Equal(DeviceTransport.Adb, _fixture.Adb.Kind);
        Assert.False(string.IsNullOrWhiteSpace(_fixture.Adb.ExecutablePath));
    }

    [SkippableFact]
    public async Task GetDevices_NeverReturnsAnUnknownDeviceId()
    {
        Skip.IfNot(File.Exists(_fixture.Adb.ExecutablePath), $"adb not found at '{_fixture.Adb.ExecutablePath}'.");

        var devices = await _fixture.Adb.GetDevicesAsync();

        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.Equal(DeviceTransport.Adb, device.Transport);
        });
    }

    [SkippableFact]
    public async Task GetDevices_MapsEveryDeviceToAKnownState()
    {
        Skip.IfNot(File.Exists(_fixture.Adb.ExecutablePath), $"adb not found at '{_fixture.Adb.ExecutablePath}'.");

        var devices = await _fixture.Adb.GetDevicesAsync();

        Assert.All(devices, device =>
            Assert.True(
                Enum.IsDefined(device.State),
                $"Device {device.Id} reported undefined state '{device.State}'."));
    }

    [SkippableFact]
    public async Task Getprop_ReportsBrandModelAndAndroidVersion()
    {
        var device = await RequireConnectedDeviceAsync();

        var output = await _fixture.Adb.ShellAsync(device.Id, "getprop");
        var properties = AdbOutputParser.ParseGetProp(output);

        Assert.False(
            string.IsNullOrWhiteSpace(properties.GetValueOrDefault("ro.product.model")),
            "ro.product.model was empty; the device did not return its identity properties.");

        Assert.False(
            string.IsNullOrWhiteSpace(properties.GetValueOrDefault("ro.build.version.release")),
            "ro.build.version.release was empty; the Android version could not be read.");
    }

    [SkippableFact]
    public async Task WmSize_ReportsAPositiveScreenResolution()
    {
        var device = await RequireConnectedDeviceAsync();

        var output = await _fixture.Adb.ShellAsync(device.Id, "wm size");
        var (width, height) = AdbOutputParser.ParseWmSize(output);

        Assert.True(width > 0 && height > 0, $"wm size returned {width}x{height} for {device.Id}.");
    }

    [SkippableFact]
    public async Task PushThenPull_RoundTripsTheFileContents()
    {
        var device = await RequireConnectedDeviceAsync();

        var remoteDirectory = "/sdcard/Download/PSMobileWallpaperIntegrationTest";
        var localSource = Path.Combine(Path.GetTempPath(), $"psmw_push_{Guid.NewGuid():N}.txt");
        var localPulled = Path.Combine(Path.GetTempPath(), $"psmw_pull_{Guid.NewGuid():N}.txt");
        var content = $"PS Mobile Wallpaper integration test {DateTime.UtcNow:O}";

        await File.WriteAllTextAsync(localSource, content);

        try
        {
            await _fixture.Adb.ShellAsync(device.Id, $"mkdir -p {remoteDirectory}");

            var remotePath = $"{remoteDirectory}/{Path.GetFileName(localSource)}";

            await _fixture.Adb.PushAsync(device.Id, localSource, remotePath);
            await _fixture.Adb.PullAsync(device.Id, remotePath, localPulled);

            Assert.True(File.Exists(localPulled), "The pulled file does not exist.");
            Assert.Equal(content, (await File.ReadAllTextAsync(localPulled)).Trim());

            await _fixture.Adb.ShellAsync(device.Id, $"rm -f {remotePath}");
        }
        finally
        {
            File.Delete(localSource);
            if (File.Exists(localPulled))
            {
                File.Delete(localPulled);
            }
        }
    }

    private async Task<DeviceInfo> RequireConnectedDeviceAsync()
    {
        Skip.IfNot(File.Exists(_fixture.Adb.ExecutablePath), $"adb not found at '{_fixture.Adb.ExecutablePath}'.");

        var devices = await _fixture.GetConnectedAsync(_fixture.Adb);

        Skip.If(
            devices.Count == 0,
            "No authorized Android device is attached over USB; connect one and re-run.");

        return devices[0];
    }
}
