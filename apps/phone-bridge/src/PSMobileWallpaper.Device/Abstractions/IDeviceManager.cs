using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Device.Abstractions;

public enum DeviceChangeKind
{
    Connected = 0,
    Disconnected = 1,
    Updated = 2,
}

public sealed class DeviceChangedEventArgs : EventArgs
{
    public required DeviceChangeKind Kind { get; init; }

    public required DeviceInfo Device { get; init; }
}

/// <summary>
/// Spec §5.1 / §25. Single source of truth for which phones are attached.
/// Brand identification lives here, never in the UI (spec §25).
/// </summary>
public interface IDeviceManager
{
    event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    /// <summary>Last known device set, without touching any device.</summary>
    IReadOnlyList<DeviceInfo> Devices { get; }

    /// <summary>Re-enumerates every enabled transport and enriches each device.</summary>
    Task<IReadOnlyList<DeviceInfo>> RefreshAsync(CancellationToken cancellationToken = default);

    Task<DeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Reads (and caches onto the device) the screen geometry, spec §6.</summary>
    Task<DisplayInfo?> GetDisplayAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Starts the background poll that discovers devices as they are plugged in (spec §5.1).</summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    Task StopMonitoringAsync();
}
