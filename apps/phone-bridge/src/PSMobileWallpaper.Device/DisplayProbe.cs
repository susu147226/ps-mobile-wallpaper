using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Transport.Adb;
using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.Device;

/// <summary>Reads screen geometry (spec §6) over either transport.</summary>
public sealed class DisplayProbe
{
    private readonly ILogger<DisplayProbe> _logger;

    public DisplayProbe(ILogger<DisplayProbe> logger) => _logger = logger;

    public async Task<DisplayInfo?> ReadAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var display = transport.Kind switch
            {
                DeviceTransport.Adb => await ReadAndroidAsync(device, transport, cancellationToken).ConfigureAwait(false),
                DeviceTransport.Hdc => await ReadHarmonyAsync(device, transport, cancellationToken).ConfigureAwait(false),
                _ => null,
            };

            if (display is null || display.Width <= 0 || display.Height <= 0)
            {
                _logger.LogWarning("Could not determine screen size for device {DeviceId}.", device.Id);
                return null;
            }

            return display;
        }
        catch (Exception ex) when (ex is TransportException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Display probe failed for device {DeviceId}.", device.Id);
            return null;
        }
    }

    private static async Task<DisplayInfo?> ReadAndroidAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var sizeOutput = await transport.ShellAsync(device.Id, "wm size", cancellationToken).ConfigureAwait(false);
        var (width, height) = AdbOutputParser.ParseWmSize(sizeOutput);

        var densityOutput = await transport.ShellAsync(device.Id, "wm density", cancellationToken).ConfigureAwait(false);
        var density = AdbOutputParser.ParseWmDensity(densityOutput);

        var rotationOutput = await transport.ShellAsync(device.Id, "dumpsys input", cancellationToken).ConfigureAwait(false);
        var rotation = AdbOutputParser.ParseRotation(rotationOutput);

        return Build(width, height, density, rotation);
    }

    private static async Task<DisplayInfo?> ReadHarmonyAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var screenOutput = await transport
            .ShellAsync(device.Id, "hidumper -s RenderService -a screen", cancellationToken)
            .ConfigureAwait(false);

        var (width, height) = HdcOutputParser.ParseScreenSize(screenOutput);

        // RenderService (the wrong place to ask) does not expose a density; DisplayManagerService does.
        var displayOutput = await transport
            .ShellAsync(device.Id, "hidumper -s DisplayManagerService -a -a", cancellationToken)
            .ConfigureAwait(false);

        var density = HdcOutputParser.ParseDensity(displayOutput);

        return Build(width, height, density, 0);
    }

    private static DisplayInfo? Build(int width, int height, int density, int rotation)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new DisplayInfo
        {
            Width = width,
            Height = height,
            Density = density,
            Rotation = rotation,
            Orientation = height >= width ? ScreenOrientation.Portrait : ScreenOrientation.Landscape,
        };
    }
}
