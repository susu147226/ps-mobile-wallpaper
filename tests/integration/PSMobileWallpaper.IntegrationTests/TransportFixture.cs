using Microsoft.Extensions.Logging.Abstractions;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Infrastructure.Configuration;
using PSMobileWallpaper.Transport;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Transport.Adb;
using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.IntegrationTests;

/// <summary>
/// Resolves adb/hdc the same way the bridge does (config, then PATH) and exposes a ready transport.
/// Tests that need real hardware call <c>Skip.IfNot</c> on the availability flags so the suite stays
/// green on a machine with no phone attached.
/// </summary>
public sealed class TransportFixture
{
    private readonly ICliProcessRunner _runner = new ProcessCliRunner(NullLogger<ProcessCliRunner>.Instance);

    public TransportFixture()
    {
        var (adbPath, hdcPath) = ReadConfiguredPaths();

        Adb = new AdbTransport(_runner, NullLogger<AdbTransport>.Instance, adbPath);
        Hdc = new HdcTransport(_runner, NullLogger<HdcTransport>.Instance, hdcPath);
    }

    public AdbTransport Adb { get; }

    public HdcTransport Hdc { get; }

    public async Task<IReadOnlyList<DeviceInfo>> GetConnectedAsync(IDeviceTransport transport, CancellationToken cancellationToken = default)
    {
        var devices = await transport.GetDevicesAsync(cancellationToken);

        return devices.Where(device => device.State == DeviceState.Connected).ToList();
    }

    /// <summary>Reads adb.path / hdc.path from the bridge's own config so the tests follow user setup.</summary>
    private static (string? Adb, string? Hdc) ReadConfiguredPaths()
    {
        try
        {
            var configuration = BridgeConfiguration.Load();

            return (
                configuration[$"{AdbOptions.SectionName}:path"],
                configuration[$"{HdcOptions.SectionName}:path"]);
        }
        catch
        {
            // Config is optional for the tests; fall back to PATH discovery.
            return (null, null);
        }
    }
}

[CollectionDefinition("transports")]
public sealed class TransportCollection : ICollectionFixture<TransportFixture>;
