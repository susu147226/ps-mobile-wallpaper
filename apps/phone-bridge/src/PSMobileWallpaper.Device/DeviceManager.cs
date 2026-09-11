using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Device.Abstractions;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Device;

/// <summary>
/// Spec §5 / §25 / §26. Aggregates every transport, applies the first matching brand adapter,
/// and publishes connect/disconnect/update events for the WebSocket layer.
/// </summary>
public sealed class DeviceManager : IDeviceManager, IAsyncDisposable
{
    private readonly IReadOnlyList<IDeviceTransport> _transports;
    private readonly IReadOnlyList<IDeviceAdapter> _adapters;
    private readonly DeviceProber _prober;
    private readonly DisplayProbe _displayProbe;
    private readonly ILogger<DeviceManager> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private Dictionary<string, DeviceInfo> _devices = new(StringComparer.Ordinal);
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    public DeviceManager(
        IEnumerable<IDeviceTransport> transports,
        IEnumerable<IDeviceAdapter> adapters,
        DeviceProber prober,
        DisplayProbe displayProbe,
        ILogger<DeviceManager> logger,
        TimeSpan? pollInterval = null)
    {
        _transports = transports.ToList();
        _adapters = adapters.ToList();
        _prober = prober;
        _displayProbe = displayProbe;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public IReadOnlyList<DeviceInfo> Devices => _devices.Values.ToList();

    public async Task<IReadOnlyList<DeviceInfo>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var discovered = new Dictionary<string, DeviceInfo>(StringComparer.Ordinal);

            foreach (var transport in _transports)
            {
                if (!await transport.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogDebug("{Transport} transport is unavailable; skipping.", transport.Kind);
                    continue;
                }

                IReadOnlyList<DeviceInfo> found;
                try
                {
                    found = await transport.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (TransportException ex)
                {
                    _logger.LogWarning(ex, "{Transport} device enumeration failed.", transport.Kind);
                    continue;
                }

                foreach (var device in found)
                {
                    if (device.State != DeviceState.Connected)
                    {
                        // Surface unauthorized/offline devices too, but without probing them.
                        discovered[device.Id] = device;
                        continue;
                    }

                    discovered[device.Id] = await EnrichAsync(device, transport, cancellationToken).ConfigureAwait(false);
                }
            }

            PublishChanges(discovered);
            _devices = discovered;

            return Devices;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_devices.TryGetValue(deviceId, out var device) ? device : null);
    }

    public async Task<DisplayInfo?> GetDisplayAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return null;
        }

        var transport = FindTransport(device);
        if (transport is null)
        {
            return device.Display;
        }

        var display = await _displayProbe.ReadAsync(device, transport, cancellationToken).ConfigureAwait(false);
        if (display is not null)
        {
            device.Display = display;
        }

        return display ?? device.Display;
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null)
        {
            return Task.CompletedTask;
        }

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorLoopAsync(_monitorCts.Token);
        _logger.LogInformation("Device monitoring started (interval {Interval}).", _pollInterval);

        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync()
    {
        if (_monitorCts is null)
        {
            return;
        }

        await _monitorCts.CancelAsync().ConfigureAwait(false);

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _monitorCts.Dispose();
        _monitorCts = null;
        _monitorTask = null;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        // Discover immediately on startup, then on every tick (spec §5.1).
        await SafeRefreshAsync(cancellationToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await SafeRefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SafeRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device refresh failed; will retry on the next tick.");
        }
    }

    private async Task<DeviceInfo> EnrichAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var probe = await _prober.ProbeAsync(device, transport, cancellationToken).ConfigureAwait(false);

        var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(probe));
        if (adapter is null)
        {
            _logger.LogDebug("No adapter claimed device {DeviceId}.", device.Id);
        }
        else
        {
            adapter.Enrich(probe);
        }

        device.Display = await _displayProbe.ReadAsync(device, transport, cancellationToken).ConfigureAwait(false);

        return device;
    }

    private IDeviceTransport? FindTransport(DeviceInfo device) =>
        _transports.FirstOrDefault(transport => transport.Kind == device.Transport);

    private void PublishChanges(Dictionary<string, DeviceInfo> discovered)
    {
        foreach (var (id, device) in discovered)
        {
            if (!_devices.ContainsKey(id))
            {
                Raise(DeviceChangeKind.Connected, device);
            }
            else if (HasChanged(_devices[id], device))
            {
                Raise(DeviceChangeKind.Updated, device);
            }
        }

        foreach (var (id, device) in _devices)
        {
            if (!discovered.ContainsKey(id))
            {
                Raise(DeviceChangeKind.Disconnected, device);
            }
        }
    }

    private static bool HasChanged(DeviceInfo before, DeviceInfo after) =>
        before.State != after.State ||
        before.Model != after.Model ||
        before.OsVersion != after.OsVersion ||
        before.Display?.Width != after.Display?.Width ||
        before.Display?.Height != after.Display?.Height;

    private void Raise(DeviceChangeKind kind, DeviceInfo device)
    {
        _logger.LogInformation("Device {DeviceId} {ChangeKind}.", device.Id, kind);
        DeviceChanged?.Invoke(this, new DeviceChangedEventArgs { Kind = kind, Device = device });
    }

    public async ValueTask DisposeAsync()
    {
        await StopMonitoringAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
    }
}
