using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Spec §18. HarmonyOS over HDC.
///
/// Verified unsupported on EMA-AL00U (OpenHarmony 7.0.0.105 / API 26) by experiment, not assumption —
/// the README carries the full evidence. In short:
///
///   * @ohos.wallpaper.setWallpaper is deprecated since API 9 and on API 26 is a stub: it reports
///     success while the lock screen and the home screen both stay unchanged.
///   * The platform's real capability sits in com.ohos.sceneboard's WallpaperServiceExtAbility, and
///     binding it needs ohos.permission.ACTIVATE_THEME_PACKAGE — declaring that permission fails the
///     install outright, so it is system-level and unreachable for a third-party app.
///   * The gallery is not a way around it either: the media library's own directory
///     (/storage/media/100/local/files/Photo) rejects hdc writes, and the only writable sibling
///     (Docs) is not indexed by the media library.
///
/// So this provider claims nothing, and refuses rather than reporting a success nobody can see.
/// </summary>
public sealed class HarmonyWallpaperProvider : WallpaperProviderBase
{
    public HarmonyWallpaperProvider(ILogger<HarmonyWallpaperProvider> logger) : base(logger) { }

    public override string Name => "HarmonyWallpaperProvider";

    public override bool CanHandle(DeviceInfo device) => device.Transport == Domain.Models.DeviceTransport.Hdc;

    public override Task<WallpaperCapabilities> GetCapabilitiesAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WallpaperCapabilities.None);

    public override Task<WallpaperResult> SetLockAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "lock screen");

    public override Task<WallpaperResult> SetHomeAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "home screen");

    public override Task<WallpaperResult> SetBothAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync(device, "lock and home screen");

    /// <summary>
    /// Refused on purpose. hdc file send into the media library reports success while writing
    /// nothing, so attempting it would hand the user a success they cannot see anywhere.
    /// </summary>
    protected override Task<WallpaperResult> SaveToGalleryAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation(
            "Refusing to save to the gallery on {DeviceId}: the HarmonyOS media library is not writable over HDC.",
            device.Id);

        return Task.FromResult(WallpaperResult.Fail(
            device.Id,
            ErrorCodes.WallpaperNotSupported,
            $"{device.DisplayName}: HarmonyOS does not allow an app to set the wallpaper, and its " +
            "media library cannot be written over HDC."));
    }
}
