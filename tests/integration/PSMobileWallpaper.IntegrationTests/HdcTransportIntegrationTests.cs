using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.IntegrationTests;

/// <summary>
/// Spec §34 Phase 4. Verifies the HDC transport against a real `hdc` binary and a real HarmonyOS
/// handset when one is attached. Hardware-dependent tests skip themselves otherwise.
/// </summary>
[Collection("transports")]
public sealed class HdcTransportIntegrationTests
{
    private readonly TransportFixture _fixture;

    public HdcTransportIntegrationTests(TransportFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task IsAvailable_WheneverHdcIsInstalled()
    {
        Skip.IfNot(File.Exists(_fixture.Hdc.ExecutablePath), $"hdc not found at '{_fixture.Hdc.ExecutablePath}'.");

        Assert.True(await _fixture.Hdc.IsAvailableAsync());
    }

    [SkippableFact]
    public void KindAndExecutablePath_AreHdc()
    {
        Assert.Equal(DeviceTransport.Hdc, _fixture.Hdc.Kind);
        Assert.False(string.IsNullOrWhiteSpace(_fixture.Hdc.ExecutablePath));
    }

    [SkippableFact]
    public async Task GetDevices_NeverReportsAHostConnectionChannel()
    {
        Skip.IfNot(File.Exists(_fixture.Hdc.ExecutablePath), $"hdc not found at '{_fixture.Hdc.ExecutablePath}'.");

        var devices = await _fixture.Hdc.GetDevicesAsync();

        // `hdc list targets -v` also lists host-side channels (e.g. a local COM port). Discovery must
        // only report attached devices, so nothing may come back as a bare host port.
        Assert.All(devices, device =>
        {
            Assert.False(
                device.Id.StartsWith("COM", StringComparison.OrdinalIgnoreCase),
                $"Host connection channel '{device.Id}' was reported as a device.");

            Assert.Equal(DeviceTransport.Hdc, device.Transport);
        });
    }

    [SkippableFact]
    public async Task GetDevices_MapsEveryDeviceToAKnownState()
    {
        Skip.IfNot(File.Exists(_fixture.Hdc.ExecutablePath), $"hdc not found at '{_fixture.Hdc.ExecutablePath}'.");

        var devices = await _fixture.Hdc.GetDevicesAsync();

        Assert.All(devices, device => Assert.True(Enum.IsDefined(device.State)));
    }

    [SkippableFact]
    public async Task ParamGet_ReportsProductIdentity()
    {
        var device = await RequireConnectedDeviceAsync();

        var model = await _fixture.Hdc.ShellAsync(device.Id, "param get const.product.model");

        Assert.False(
            string.IsNullOrWhiteSpace(HdcOutputParser.ParseParamGet(model)),
            "const.product.model was empty; the device did not return its identity properties.");
    }

    [SkippableFact]
    public async Task Hidumper_ReportsAPositiveScreenSize()
    {
        var device = await RequireConnectedDeviceAsync();

        var output = await _fixture.Hdc.ShellAsync(device.Id, "hidumper -s RenderService -a screen");
        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.True(width > 0 && height > 0, $"Screen dump returned {width}x{height} for {device.Id}.");
    }

    [SkippableFact]
    public async Task FileSendThenReceive_RoundTripsTheFileContents()
    {
        var device = await RequireConnectedDeviceAsync();

        var remotePath = "/data/local/tmp/psmw_integration_test.txt";
        var localSource = Path.Combine(Path.GetTempPath(), $"psmw_hdc_send_{Guid.NewGuid():N}.txt");
        var localReceived = Path.Combine(Path.GetTempPath(), $"psmw_hdc_recv_{Guid.NewGuid():N}.txt");
        var content = $"PS Mobile Wallpaper HDC integration test {DateTime.UtcNow:O}";

        await File.WriteAllTextAsync(localSource, content);

        try
        {
            await _fixture.Hdc.PushAsync(device.Id, localSource, remotePath);
            await _fixture.Hdc.PullAsync(device.Id, remotePath, localReceived);

            Assert.True(File.Exists(localReceived), "The received file does not exist.");
            Assert.Equal(content, (await File.ReadAllTextAsync(localReceived)).Trim());

            await _fixture.Hdc.ShellAsync(device.Id, $"rm -f {remotePath}");
        }
        finally
        {
            File.Delete(localSource);
            if (File.Exists(localReceived))
            {
                File.Delete(localReceived);
            }
        }
    }

    private async Task<DeviceInfo> RequireConnectedDeviceAsync()
    {
        Skip.IfNot(File.Exists(_fixture.Hdc.ExecutablePath), $"hdc not found at '{_fixture.Hdc.ExecutablePath}'.");

        var devices = await _fixture.GetConnectedAsync(_fixture.Hdc);

        Skip.If(
            devices.Count == 0,
            "No authorized HarmonyOS device is attached over USB; connect one and re-run.");

        return devices[0];
    }
}
