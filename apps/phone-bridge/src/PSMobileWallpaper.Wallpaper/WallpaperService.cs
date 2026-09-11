using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Wallpaper.Abstractions;

namespace PSMobileWallpaper.Wallpaper;

/// <summary>
/// Spec §17 / §18 / §20. Picks the first provider that claims the device and applies the
/// configured gallery behaviour around every wallpaper operation.
/// </summary>
public sealed class WallpaperService : IWallpaperService
{
    private readonly IReadOnlyList<IWallpaperProvider> _providers;
    private readonly IReadOnlyList<IDeviceTransport> _transports;
    private readonly ILogger<WallpaperService> _logger;
    private readonly bool _saveToGallery;

    public WallpaperService(
        IEnumerable<IWallpaperProvider> providers,
        IEnumerable<IDeviceTransport> transports,
        ILogger<WallpaperService> logger,
        bool saveToGallery = true)
    {
        _providers = providers.ToList();
        _transports = transports.ToList();
        _logger = logger;
        _saveToGallery = saveToGallery;
    }

    public async Task<WallpaperCapabilities> GetCapabilitiesAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(device);
        if (provider is null)
        {
            return WallpaperCapabilities.None;
        }

        var transport = _transports.FirstOrDefault(candidate => candidate.Kind == device.Transport);
        if (transport is null)
        {
            return WallpaperCapabilities.None;
        }

        return await provider
            .GetCapabilitiesAsync(device, transport, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<WallpaperResult> SetLockWallpaperAsync(
        DeviceInfo device,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(device, imagePath, (provider, transport) =>
            provider.SetLockAsync(device, transport, imagePath, cancellationToken), cancellationToken);

    public Task<WallpaperResult> SetHomeWallpaperAsync(
        DeviceInfo device,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(device, imagePath, (provider, transport) =>
            provider.SetHomeAsync(device, transport, imagePath, cancellationToken), cancellationToken);

    public Task<WallpaperResult> SetBothWallpaperAsync(
        DeviceInfo device,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(device, imagePath, (provider, transport) =>
            provider.SetBothAsync(device, transport, imagePath, cancellationToken), cancellationToken);

    private async Task<WallpaperResult> ExecuteAsync(
        DeviceInfo device,
        string imagePath,
        Func<IWallpaperProvider, IDeviceTransport, Task<WallpaperResult>> operation,
        CancellationToken cancellationToken)
    {
        var provider = FindProvider(device);
        if (provider is null)
        {
            _logger.LogWarning("No wallpaper provider handles device {DeviceId}.", device.Id);
            return WallpaperResult.Fail(
                device.Id,
                ErrorCodes.WallpaperNotSupported,
                $"No wallpaper provider supports {device.DisplayName}.");
        }

        var transport = _transports.FirstOrDefault(candidate => candidate.Kind == device.Transport);
        if (transport is null)
        {
            return WallpaperResult.Fail(
                device.Id,
                ErrorCodes.TransportError,
                $"No {device.Transport} transport is registered.");
        }

        if (!File.Exists(imagePath))
        {
            return WallpaperResult.Fail(device.Id, ErrorCodes.ImageNotFound, $"Image not found: {imagePath}");
        }

        // Spec §20: the default mode saves to the gallery before touching wallpaper settings.
        if (_saveToGallery)
        {
            var capabilities = await provider
                .GetCapabilitiesAsync(device, transport, cancellationToken)
                .ConfigureAwait(false);

            if (capabilities.CanSaveToGallery)
            {
                var saveResult = await SaveToGalleryAsync(provider, device, transport, imagePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!saveResult.Success)
                {
                    return saveResult;
                }
            }
        }

        return await operation(provider, transport).ConfigureAwait(false);
    }

    private async Task<WallpaperResult> SaveToGalleryAsync(
        IWallpaperProvider provider,
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken)
    {
        // The provider base exposes gallery save through the same shell/push path; call it directly
        // so providers that support gallery keep control over their own destination directory.
        if (provider is Providers.WallpaperProviderBase providerBase)
        {
            return await providerBase
                .SaveToGalleryForServiceAsync(device, transport, imagePath, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogDebug("{Provider} does not implement gallery save.", provider.Name);
        return WallpaperResult.Ok(device.Id);
    }

    private IWallpaperProvider? FindProvider(DeviceInfo device) =>
        _providers.FirstOrDefault(provider => provider.CanHandle(device));
}
