using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Image;
using PSMobileWallpaper.Image.Abstractions;
using PSMobileWallpaper.Wallpaper.Abstractions;
using DeviceManager = PSMobileWallpaper.Device.Abstractions.IDeviceManager;

namespace PSMobileWallpaper.Application;

/// <summary>Outcome of the pre-flight crop stage (spec §21 <c>/wallpaper/prepare</c>).</summary>
public sealed record PreparedWallpaper(bool Success, string? ImagePath, int Width, int Height, string Message, string? ErrorCode);

/// <summary>
/// Spec §10 / §13 / §17 / §21. Drives the full "canvas -> phone wallpaper" flow and is the only
/// place that sequences image processing, transfer and wallpaper assignment.
/// </summary>
public sealed class WallpaperWorkflow
{
    private readonly DeviceManager _deviceManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly IWallpaperService _wallpaperService;
    private readonly ILogger<WallpaperWorkflow> _logger;

    public WallpaperWorkflow(
        DeviceManager deviceManager,
        IImageProcessor imageProcessor,
        IWallpaperService wallpaperService,
        ILogger<WallpaperWorkflow> logger)
    {
        _deviceManager = deviceManager;
        _imageProcessor = imageProcessor;
        _wallpaperService = wallpaperService;
        _logger = logger;
    }

    /// <summary>Reads the phone's screen size and crops the source image to match (spec §10 / §11).</summary>
    public async Task<PreparedWallpaper> PrepareAsync(
        string deviceId,
        string sourceImagePath,
        int? widthOverride = null,
        int? heightOverride = null,
        CropMode mode = CropMode.CenterCrop,
        CropRect? customRegion = null,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return new PreparedWallpaper(false, null, 0, 0, $"Device '{deviceId}' was not found.", ErrorCodes.DeviceNotFound);
        }

        if (!File.Exists(sourceImagePath))
        {
            return new PreparedWallpaper(false, null, 0, 0, $"Image not found: {sourceImagePath}", ErrorCodes.ImageNotFound);
        }

        var targetWidth = widthOverride ?? device.Display?.Width ?? 0;
        var targetHeight = heightOverride ?? device.Display?.Height ?? 0;

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            var display = await _deviceManager.GetDisplayAsync(deviceId, cancellationToken).ConfigureAwait(false);
            targetWidth = display?.Width ?? 0;
            targetHeight = display?.Height ?? 0;
        }

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return new PreparedWallpaper(
                false, null, 0, 0,
                $"Could not determine the screen size of '{device.DisplayName}'.",
                ErrorCodes.DisplayInfoFailed);
        }

        try
        {
            var outputPath = await _imageProcessor
                .CropAsync(sourceImagePath, targetWidth, targetHeight, mode, customRegion, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Prepared {Width}x{Height} wallpaper for {DeviceId} with mode {Mode} at '{OutputPath}'.",
                targetWidth, targetHeight, deviceId, mode, outputPath);

            return new PreparedWallpaper(
                true, outputPath, targetWidth, targetHeight,
                $"Prepared {targetWidth}x{targetHeight} wallpaper using {CropModes.ToConfigName(mode)}.", null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            _logger.LogError(ex, "Preparing the wallpaper for {DeviceId} failed.", deviceId);
            return new PreparedWallpaper(false, null, 0, 0, ex.Message, ErrorCodes.ImageProcessFailed);
        }
    }

    /// <summary>Spec §21 <c>/wallpaper/send</c>: pushes an already-prepared image to the device gallery.</summary>
    public async Task<WallpaperResult> SendAsync(
        string deviceId,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return WallpaperResult.Fail(deviceId, ErrorCodes.DeviceNotFound, $"Device '{deviceId}' was not found.");
        }

        var capabilities = await _wallpaperService
            .GetCapabilitiesAsync(device, cancellationToken)
            .ConfigureAwait(false);

        if (!capabilities.CanSaveToGallery)
        {
            return WallpaperResult.Fail(
                deviceId,
                ErrorCodes.WallpaperNotSupported,
                $"Sending to the gallery is not supported for {device.DisplayName}.");
        }

        // SetLock is the closest existing operation that performs a gallery save; providers that
        // cannot set the lock screen still complete the gallery half and report the rest as unsupported.
        return await _wallpaperService
            .SetLockWallpaperAsync(device, imagePath, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<WallpaperResult> SetLockAsync(string deviceId, string imagePath, CancellationToken cancellationToken = default) =>
        ApplyAsync(deviceId, imagePath, (device, path) =>
            _wallpaperService.SetLockWallpaperAsync(device, path, cancellationToken), cancellationToken);

    public Task<WallpaperResult> SetHomeAsync(string deviceId, string imagePath, CancellationToken cancellationToken = default) =>
        ApplyAsync(deviceId, imagePath, (device, path) =>
            _wallpaperService.SetHomeWallpaperAsync(device, path, cancellationToken), cancellationToken);

    public Task<WallpaperResult> SetBothAsync(string deviceId, string imagePath, CancellationToken cancellationToken = default) =>
        ApplyAsync(deviceId, imagePath, (device, path) =>
            _wallpaperService.SetBothWallpaperAsync(device, path, cancellationToken), cancellationToken);

    private async Task<WallpaperResult> ApplyAsync(
        string deviceId,
        string imagePath,
        Func<DeviceInfo, string, Task<WallpaperResult>> operation,
        CancellationToken cancellationToken)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return WallpaperResult.Fail(deviceId, ErrorCodes.DeviceNotFound, $"Device '{deviceId}' was not found.");
        }

        if (!File.Exists(imagePath))
        {
            return WallpaperResult.Fail(deviceId, ErrorCodes.ImageNotFound, $"Image not found: {imagePath}");
        }

        return await operation(device, imagePath).ConfigureAwait(false);
    }
}
