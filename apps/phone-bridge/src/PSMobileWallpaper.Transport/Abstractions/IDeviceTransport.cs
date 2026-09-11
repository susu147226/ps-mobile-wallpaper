using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Transport.Abstractions;

/// <summary>
/// Spec §13 / §14. The single seam through which the bridge reaches a phone.
/// No layer above Transport may invoke adb.exe or hdc.exe directly (spec §40).
/// </summary>
public interface IDeviceTransport
{
    /// <summary>Which transport this instance speaks.</summary>
    DeviceTransport Kind { get; }

    /// <summary>Resolved path to adb.exe / hdc.exe.</summary>
    string ExecutablePath { get; }

    /// <summary>True when the backing executable exists and responds to its version probe (spec §15 / §16).</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Enumerates attached devices, already mapped to the unified <see cref="DeviceInfo"/> model (spec §5.2 / §5.3).</summary>
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a shell command on the device and returns its stdout.</summary>
    Task<string> ShellAsync(string deviceId, string command, CancellationToken cancellationToken = default);

    /// <summary>Sends a local file to the device.</summary>
    Task PushAsync(string deviceId, string localPath, string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a file from the device.</summary>
    Task PullAsync(string deviceId, string remotePath, string localPath, CancellationToken cancellationToken = default);
}
