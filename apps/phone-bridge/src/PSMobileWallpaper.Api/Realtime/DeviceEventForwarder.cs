using PSMobileWallpaper.Device.Abstractions;

namespace PSMobileWallpaper.Api.Realtime;

/// <summary>
/// Spec §22. Bridges <see cref="IDeviceManager.DeviceChanged"/> onto the WebSocket fan-out and
/// owns the device monitor's lifetime.
/// </summary>
public sealed class DeviceEventForwarder : IHostedService
{
    private readonly IDeviceManager _deviceManager;
    private readonly EventBroadcaster _broadcaster;
    private readonly ILogger<DeviceEventForwarder> _logger;

    public DeviceEventForwarder(
        IDeviceManager deviceManager,
        EventBroadcaster broadcaster,
        ILogger<DeviceEventForwarder> logger)
    {
        _deviceManager = deviceManager;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _deviceManager.DeviceChanged += OnDeviceChanged;
        await _deviceManager.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Device event forwarding started.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _deviceManager.DeviceChanged -= OnDeviceChanged;
        await _deviceManager.StopMonitoringAsync().ConfigureAwait(false);

        _logger.LogInformation("Device event forwarding stopped.");
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs args)
    {
        var eventName = args.Kind switch
        {
            DeviceChangeKind.Connected => BridgeEventNames.DeviceConnected,
            DeviceChangeKind.Disconnected => BridgeEventNames.DeviceDisconnected,
            _ => BridgeEventNames.DeviceUpdated,
        };

        var payload = new DeviceEventPayload(args.Device.Id, args.Device.Brand, args.Device.Model);

        // Fire-and-forget: the device monitor must not wait on socket writes.
        _ = _broadcaster
            .PublishAsync(EventBroadcaster.Create(eventName, payload))
            .ContinueWith(
                task => _logger.LogDebug(task.Exception, "Publishing '{EventName}' failed.", eventName),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}
