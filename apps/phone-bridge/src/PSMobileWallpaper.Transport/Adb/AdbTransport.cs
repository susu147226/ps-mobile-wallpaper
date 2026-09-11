using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Transport.Adb;

/// <summary>Spec §15. All Android access goes through here; nothing above may call adb.exe (spec §40).</summary>
public sealed class AdbTransport : IDeviceTransport
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);

    private readonly ICliProcessRunner _runner;
    private readonly ILogger<AdbTransport> _logger;

    public AdbTransport(
        ICliProcessRunner runner,
        ILogger<AdbTransport> logger,
        string? configuredPath = null,
        string? bundledDirectory = null)
    {
        _runner = runner;
        _logger = logger;
        ExecutablePath = ExecutableLocator.Resolve("adb", configuredPath, bundledDirectory ?? string.Empty);
    }

    public DeviceTransport Kind => DeviceTransport.Adb;

    public string ExecutablePath { get; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath))
        {
            _logger.LogWarning("adb is not available at '{Path}'.", ExecutablePath);
            return false;
        }

        try
        {
            var result = await _runner
                .RunAsync(ExecutablePath, ["version"], ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _logger.LogWarning("'adb version' exited with {ExitCode}.", result.ExitCode);
            }

            return result.Succeeded;
        }
        catch (TransportException ex)
        {
            _logger.LogWarning(ex, "adb probe failed.");
            return false;
        }
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner
            .RunAsync(ExecutablePath, ["devices", "-l"], ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new TransportException(
                ErrorCodes.TransportError,
                $"'adb devices' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        var raw = AdbOutputParser.ParseDevices(result.StandardOutput);
        _logger.LogDebug("adb reported {Count} device(s).", raw.Count);

        var devices = new List<DeviceInfo>(raw.Count);
        foreach (var device in raw)
        {
            devices.Add(new DeviceInfo
            {
                Id = device.Id,
                Transport = DeviceTransport.Adb,
                State = MapState(device.State),
            });
        }

        return devices;
    }

    public Task<string> ShellAsync(string deviceId, string command, CancellationToken cancellationToken = default) =>
        RunCaptureAsync(["-s", deviceId, "shell", command], ShellTimeout, "shell", cancellationToken);

    public Task<string> ShellWithArgsAsync(
        string deviceId,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "-s", deviceId, "shell" };
        arguments.AddRange(command);
        return RunCaptureAsync(arguments, ShellTimeout, "shell", cancellationToken);
    }

    public async Task PushAsync(
        string deviceId,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner
            .RunAsync(ExecutablePath, ["-s", deviceId, "push", localPath, remotePath], TransferTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new TransportException(
                ErrorCodes.ImageUploadFailed,
                $"'adb push' to '{remotePath}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        _logger.LogInformation("Pushed '{LocalPath}' to {DeviceId}:{RemotePath}.", localPath, deviceId, remotePath);
    }

    public async Task PullAsync(
        string deviceId,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner
            .RunAsync(ExecutablePath, ["-s", deviceId, "pull", remotePath, localPath], TransferTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new TransportException(
                ErrorCodes.TransportError,
                $"'adb pull' of '{remotePath}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        _logger.LogInformation("Pulled '{RemotePath}' from {DeviceId} to '{LocalPath}'.", remotePath, deviceId, localPath);
    }

    private async Task<string> RunCaptureAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await _runner
            .RunAsync(ExecutablePath, arguments, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new TransportException(
                ErrorCodes.TransportError,
                $"'{operation}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput;
    }

    private static DeviceState MapState(string state) => state switch
    {
        "Connected" => DeviceState.Connected,
        "Offline" => DeviceState.Offline,
        "Unauthorized" => DeviceState.Unauthorized,
        _ => DeviceState.Unknown,
    };
}
