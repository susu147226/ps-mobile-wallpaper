using PSMobileWallpaper.Device.Abstractions;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.Api.Realtime;

/// <summary>
/// Establishes the reverse port forward that lets a HarmonyOS phone reach this bridge.
///
/// The phone-side helper app downloads the prepared wallpaper over loopback, which only works
/// because `hdc rport tcp:18766 tcp:18765` makes the phone's own port 18766 reach this machine.
/// Without it the app has nothing to fetch, so the bridge sets it up itself rather than leaving a
/// command for the user to remember on every new machine.
///
/// Done per device, on connect, because hdc needs a target and there may be none at startup.
/// </summary>
public sealed class HarmonyPortForwardService : IHostedService
{
    /// <summary>Port the phone listens on. Distinct from the bridge's own 18765 so the intent is obvious.</summary>
    private const int DevicePort = 18766;

    private readonly IDeviceManager _deviceManager;
    private readonly ICliProcessRunner _runner;
    private readonly ILogger<HarmonyPortForwardService> _logger;
    private readonly string? _hdcPath;
    private readonly int _hostPort;

    private readonly HashSet<string> _forwarded = new(StringComparer.Ordinal);

    public HarmonyPortForwardService(
        IDeviceManager deviceManager,
        IEnumerable<IDeviceTransport> transports,
        ICliProcessRunner runner,
        Microsoft.Extensions.Options.IOptions<Infrastructure.Configuration.ServerOptions> serverOptions,
        ILogger<HarmonyPortForwardService> logger)
    {
        _deviceManager = deviceManager;
        _runner = runner;
        _logger = logger;
        _hostPort = serverOptions.Value.Port;
        _hdcPath = transports.OfType<HdcTransport>().FirstOrDefault()?.ExecutablePath;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_hdcPath) || !File.Exists(_hdcPath))
        {
            _logger.LogInformation(
                "hdc is not available, so the HarmonyOS reverse port forward is skipped.");
            return;
        }

        _deviceManager.DeviceChanged += OnDeviceChanged;

        // Devices already attached when the bridge starts need the forward too.
        foreach (var device in _deviceManager.Devices)
        {
            await EnsureForwardAsync(device, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "HarmonyOS reverse port forward enabled: device tcp:{DevicePort} -> host tcp:{HostPort}.",
            DevicePort,
            _hostPort);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _deviceManager.DeviceChanged -= OnDeviceChanged;

        return Task.CompletedTask;
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs args)
    {
        if (args.Kind == DeviceChangeKind.Connected)
        {
            _ = EnsureForwardAsync(args.Device, CancellationToken.None);
        }
        else if (args.Kind == DeviceChangeKind.Disconnected)
        {
            _forwarded.Remove(args.Device.Id);
        }
    }

    private async Task EnsureForwardAsync(DeviceInfo device, CancellationToken cancellationToken)
    {
        if (device.Transport != DeviceTransport.Hdc || _forwarded.Contains(device.Id) || _hdcPath is null)
        {
            return;
        }

        try
        {
            // Re-issuing an existing forward is harmless, so no attempt is made to detect one first.
            var result = await _runner
                .RunAsync(
                    _hdcPath,
                    ["-t", device.Id, "rport", $"tcp:{DevicePort}", $"tcp:{_hostPort}"],
                    TimeSpan.FromSeconds(15),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded && result.StandardOutput.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                _forwarded.Add(device.Id);
                _logger.LogInformation(
                    "Reverse port forward established for {DeviceId}: device tcp:{DevicePort} -> host tcp:{HostPort}.",
                    device.Id,
                    DevicePort,
                    _hostPort);
            }
            else
            {
                _logger.LogWarning(
                    "Reverse port forward for {DeviceId} failed: {Output}",
                    device.Id,
                    result.CombinedOutput.Trim());
            }
        }
        catch (TransportException ex)
        {
            _logger.LogWarning(ex, "Reverse port forward for {DeviceId} failed.", device.Id);
        }
    }
}
