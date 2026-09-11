using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Base for Android providers that set wallpapers through the bundled companion app.
///
/// `adb shell` cannot set a wallpaper: `cmd wallpaper` has no implementation on these builds, and
/// although the shell user holds SET_WALLPAPER there is no CLI surface that uses it. The helper app
/// performs the operation through the official WallpaperManager API instead — no root, no system
/// modification, no security bypass (spec §42).
///
/// Capabilities are only advertised once the helper is actually installed on the device, so a phone
/// without it honestly reports gallery-only support (spec §40).
/// </summary>
public abstract class AndroidHelperWallpaperProviderBase : WallpaperProviderBase
{
    internal const string HelperPackage = "com.psmobilewallpaper.helper";
    internal const string HelperActivity = "com.psmobilewallpaper.helper.SetWallpaperActivity";

    /// <summary>
    /// Where the image is pushed before the helper is invoked.
    ///
    /// Deliberately NOT /sdcard/Android/data/&lt;pkg&gt;: since Android 11 that tree is unreachable
    /// through a raw path even for the app that owns it, so File.Exists() inside the helper returns
    /// false. Shared storage works, and the helper is installed with `-g` so READ_EXTERNAL_STORAGE
    /// is already granted.
    /// </summary>
    private const string RemoteImageDirectory = "/sdcard/Download/PSMobileWallpaper";

    /// <summary>
    /// Written by the helper into its private files directory: `am start` cannot return a value.
    /// Read back with `run-as`, because since Android 11 the shell cannot see /sdcard/Android/data.
    ///
    /// `|| true` is load-bearing: while the helper is still starting, the file does not exist yet and
    /// `cat` exits non-zero, which the transport would otherwise surface as a hard failure.
    /// </summary>
    private const string ResultCommand =
        $"run-as {HelperPackage} cat files/psmw-result.json 2>/dev/null || true";

    private static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromMilliseconds(500);

    protected AndroidHelperWallpaperProviderBase(ILogger logger, string helperApkPath) : base(logger)
        => HelperApkPath = helperApkPath;

    /// <summary>Absolute path to the helper APK shipped alongside the bridge. Empty when unavailable.</summary>
    protected string HelperApkPath { get; }

    /// <summary>
    /// Whether the LOCK screen can actually be set on this device.
    ///
    /// Verified FALSE on Huawei EMUI (JAD-AL80, Android 12): the helper's
    /// setStream(..., FLAG_LOCK) call reports success, the AOSP wallpaper service even records a new
    /// lock-wallpaper id in `dumpsys wallpaper`, and the lock screen still does not change. EMUI
    /// renders its lock wallpaper from its own theme engine, behind the signature-level permission
    /// com.huawei.android.thememanager.permission.THEME_PROVIDER_ACCESS, which a third-party app
    /// cannot hold.
    ///
    /// Do not flip this to true on the strength of the API returning success, or of dumpsys showing
    /// a lock id: neither reflects what the device actually displays. It needs a visual check on the
    /// lock screen itself (spec §40).
    /// </summary>
    protected virtual bool LockScreenIsSettable => false;

    public override async Task<WallpaperCapabilities> GetCapabilitiesAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default)
    {
        var helperInstalled = await IsHelperInstalledAsync(device, transport, cancellationToken).ConfigureAwait(false);

        // The home screen uses the public setStream overload and is confirmed working; the lock
        // screen depends on the OEM honouring FLAG_LOCK, which EMUI does not.
        var canSetHome = helperInstalled;
        var canSetLock = helperInstalled && LockScreenIsSettable;

        return new WallpaperCapabilities
        {
            CanSetLock = canSetLock,
            CanSetHome = canSetHome,
            CanSetBoth = canSetHome && canSetLock,
            CanSaveToGallery = true,
            RequiresUserConfirmation = !helperInstalled,
        };
    }

    public override Task<WallpaperResult> SetLockAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        LockScreenIsSettable
            ? ApplyAsync(device, transport, imagePath, "lock", cancellationToken)
            : RefuseLockAsync(device);

    public override Task<WallpaperResult> SetHomeAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(device, transport, imagePath, "home", cancellationToken);

    public override Task<WallpaperResult> SetBothAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        LockScreenIsSettable
            ? ApplyAsync(device, transport, imagePath, "both", cancellationToken)
            : RefuseLockAsync(device);

    /// <summary>
    /// Refuse rather than attempt: the helper would report success while the lock screen stayed
    /// unchanged, which is exactly the false positive spec §40 forbids.
    /// </summary>
    private Task<WallpaperResult> RefuseLockAsync(DeviceInfo device)
    {
        Logger.LogInformation(
            "Refusing to set the lock wallpaper on {DeviceId}: this OEM does not honour FLAG_LOCK.",
            device.Id);

        return Task.FromResult(WallpaperResult.Fail(
            device.Id,
            ErrorCodes.WallpaperNotSupported,
            $"{device.DisplayName} does not let third-party apps change the lock-screen wallpaper. " +
            "The home-screen wallpaper is supported."));
    }

    /// <summary>True when the helper package is present on the device.</summary>
    protected async Task<bool> IsHelperInstalledAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await transport
                .ShellAsync(device.Id, $"pm list packages {HelperPackage}", cancellationToken)
                .ConfigureAwait(false);

            return output.Contains(HelperPackage, StringComparison.Ordinal);
        }
        catch (TransportException ex)
        {
            Logger.LogDebug(ex, "Could not query installed packages on {DeviceId}.", device.Id);
            return false;
        }
    }

    private async Task<WallpaperResult> ApplyAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        string target,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            return WallpaperResult.Fail(device.Id, ErrorCodes.ImageNotFound, $"Image not found: {imagePath}");
        }

        var install = await EnsureHelperInstalledAsync(device, transport, cancellationToken).ConfigureAwait(false);
        if (install is not null)
        {
            return install;
        }

        try
        {
            await transport
                .ShellAsync(device.Id, $"mkdir -p {RemoteImageDirectory}", cancellationToken)
                .ConfigureAwait(false);

            var remoteImage = $"{RemoteImageDirectory}/{Path.GetFileName(imagePath)}";
            await transport.PushAsync(device.Id, imagePath, remoteImage, cancellationToken).ConfigureAwait(false);

            // Clear any previous result so a stale file cannot be mistaken for this run's outcome.
            await transport
                .ShellAsync(device.Id, $"run-as {HelperPackage} rm -f files/psmw-result.json", cancellationToken)
                .ConfigureAwait(false);

            var command =
                $"am start -n {HelperPackage}/{HelperActivity}" +
                $" --es imagePath {remoteImage}" +
                $" --es target {target}";

            var startOutput = await transport.ShellAsync(device.Id, command, cancellationToken).ConfigureAwait(false);
            Logger.LogDebug("Helper start output for {DeviceId}: {Output}", device.Id, startOutput.Trim());

            var payload = await WaitForResultAsync(device, transport, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return WallpaperResult.Fail(
                    device.Id,
                    ErrorCodes.WallpaperSetFailed,
                    "The wallpaper helper did not report a result. It may be blocked from starting on this device.");
            }

            Logger.LogInformation(
                "Helper result for {DeviceId} ({Target}): success={Success} applied={Applied} message={Message}",
                device.Id, target, payload.Success, string.Join(",", payload.Applied ?? []), payload.Message);

            return payload.Success
                ? WallpaperResult.Ok(device.Id, payload.Message ?? $"Applied to {target}.")
                : WallpaperResult.Fail(
                    device.Id,
                    payload.ErrorCode ?? ErrorCodes.WallpaperSetFailed,
                    payload.Message ?? "The helper reported a failure.");
        }
        catch (TransportException ex)
        {
            Logger.LogError(ex, "Setting the {Target} wallpaper on {DeviceId} failed.", target, device.Id);
            return WallpaperResult.Fail(device.Id, ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>Installs the bundled helper when it is missing. Returns a failure result, or null on success.</summary>
    private async Task<WallpaperResult?> EnsureHelperInstalledAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        if (await IsHelperInstalledAsync(device, transport, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(HelperApkPath) || !File.Exists(HelperApkPath))
        {
            return WallpaperResult.Fail(
                device.Id,
                ErrorCodes.WallpaperNotSupported,
                "The wallpaper helper is not installed on the device and no APK was found to install it.");
        }

        Logger.LogInformation("Installing the wallpaper helper on {DeviceId}...", device.Id);

        var remoteApk = $"{RemoteImageDirectory}/psmw-helper.apk";
        await transport.ShellAsync(device.Id, $"mkdir -p {RemoteImageDirectory}", cancellationToken).ConfigureAwait(false);
        await transport.PushAsync(device.Id, HelperApkPath, remoteApk, cancellationToken).ConfigureAwait(false);

        var output = await transport
            .ShellAsync(device.Id, $"pm install -r -g {remoteApk}", cancellationToken)
            .ConfigureAwait(false);

        // `pm install` reports on stdout; a Success line means the package manager accepted it.
        if (!output.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Helper installation on {DeviceId} failed: {Output}", device.Id, output.Trim());

            return WallpaperResult.Fail(
                device.Id,
                ErrorCodes.WallpaperSetFailed,
                "Could not install the wallpaper helper on the device. Confirm the USB debugging prompt on the phone and try again.");
        }

        Logger.LogInformation("Wallpaper helper installed on {DeviceId}.", device.Id);
        return null;
    }

    private async Task<HelperResult?> WaitForResultAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ResultTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await transport
                    .ShellAsync(device.Id, ResultCommand, cancellationToken)
                    .ConfigureAwait(false);

                var parsed = TryParse(json);
                if (parsed is not null)
                {
                    await transport
                        .ShellAsync(device.Id, $"run-as {HelperPackage} rm -f files/psmw-result.json", cancellationToken)
                        .ConfigureAwait(false);

                    return parsed;
                }
            }
            catch (TransportException ex)
            {
                // The device may still be bringing the helper up; keep polling until the deadline.
                Logger.LogDebug(ex, "Result poll for {DeviceId} failed; retrying.", device.Id);
            }

            await Task.Delay(ResultPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private HelperResult? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
        {
            // `cat` of a missing file prints nothing, but some shells still emit an error line.
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HelperResult>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "Could not parse the helper result payload.");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record HelperResult(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("errorCode")] string? ErrorCode,
        [property: JsonPropertyName("applied")] string[]? Applied);
}
