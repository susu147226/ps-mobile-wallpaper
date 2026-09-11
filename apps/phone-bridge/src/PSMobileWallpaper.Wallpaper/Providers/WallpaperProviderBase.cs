using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Shared provider behaviour. "Save to gallery" is always available, because pushing a file and
/// letting the media indexer pick it up needs no vendor cooperation. Anything beyond that must be
/// proven on real hardware before it is claimed (spec §40).
/// </summary>
public abstract class WallpaperProviderBase : Abstractions.IWallpaperProvider
{
    protected WallpaperProviderBase(ILogger logger) => Logger = logger;

    protected ILogger Logger { get; }

    public abstract string Name { get; }

    /// <summary>Gallery directory on the device. Providers that target a different OS override this.</summary>
    protected virtual string GalleryDirectory => "/sdcard/Pictures/PSMobileWallpaper";

    public abstract bool CanHandle(DeviceInfo device);

    public virtual Task<WallpaperCapabilities> GetCapabilitiesAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WallpaperCapabilities
        {
            CanSetLock = false,
            CanSetHome = false,
            CanSetBoth = false,
            CanSaveToGallery = true,
            RequiresUserConfirmation = true,
        });

    public virtual Task<WallpaperResult> SetLockAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "lock screen");

    public virtual Task<WallpaperResult> SetHomeAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "home screen");

    public virtual Task<WallpaperResult> SetBothAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "lock and home screen");

    /// <summary>Entry point used by <see cref="WallpaperService"/> for the spec §20 gallery-first flow.</summary>
    public Task<WallpaperResult> SaveToGalleryForServiceAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        SaveToGalleryAsync(device, transport, imagePath, cancellationToken);

    /// <summary>Copies the image into the device gallery and asks the media scanner to index it (spec §20).</summary>
    protected async Task<WallpaperResult> SaveToGalleryAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            return WallpaperResult.Fail(device.Id, ErrorCodes.ImageNotFound, $"Image not found: {imagePath}");
        }

        try
        {
            await transport
                .ShellAsync(device.Id, $"mkdir -p {GalleryDirectory}", cancellationToken)
                .ConfigureAwait(false);

            var remotePath = $"{GalleryDirectory}/{Path.GetFileName(imagePath)}";

            await transport.PushAsync(device.Id, imagePath, remotePath, cancellationToken).ConfigureAwait(false);
            await TriggerMediaScanAsync(device, transport, remotePath, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Saved '{ImagePath}' to gallery on {DeviceId}.", imagePath, device.Id);

            return WallpaperResult.Ok(device.Id, $"Saved to gallery at {remotePath}");
        }
        catch (TransportException ex)
        {
            Logger.LogError(ex, "Saving '{ImagePath}' to the gallery of {DeviceId} failed.", imagePath, device.Id);
            return WallpaperResult.Fail(device.Id, ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// The legacy broadcast is honoured on older Android only. Failure here is non-fatal:
    /// the file is already on disk, so the gallery picks it up on its next index pass.
    /// </summary>
    protected virtual async Task TriggerMediaScanAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string remotePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await transport
                .ShellAsync(
                    device.Id,
                    $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d file://{remotePath}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TransportException ex)
        {
            Logger.LogDebug(ex, "Media scan broadcast failed for '{RemotePath}'; the file is still on disk.", remotePath);
        }
    }

    protected Task<WallpaperResult> UnsupportedAsync(DeviceInfo device, string what)
    {
        Logger.LogInformation("Setting {What} is not supported on {DeviceId} ({Model}).", what, device.Id, device.Model);

        return Task.FromResult(WallpaperResult.Fail(
            device.Id,
            ErrorCodes.WallpaperNotSupported,
            $"Setting the {what} wallpaper is not supported for {device.DisplayName} yet."));
    }
}
