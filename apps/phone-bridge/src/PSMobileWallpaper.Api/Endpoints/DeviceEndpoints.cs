using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Wallpaper.Abstractions;
using DeviceManager = PSMobileWallpaper.Device.Abstractions.IDeviceManager;

namespace PSMobileWallpaper.Api.Endpoints;

/// <summary>Spec §21, device routes.</summary>
public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/devices").WithTags("Devices");

        group.MapGet("/", async (DeviceManager devices, CancellationToken cancellationToken) =>
            Results.Ok(await devices.RefreshAsync(cancellationToken)));

        group.MapGet("/{deviceId}", async (
            string deviceId,
            DeviceManager devices,
            CancellationToken cancellationToken) =>
        {
            var device = await devices.GetDeviceAsync(deviceId, cancellationToken);

            return device is null
                ? Results.NotFound(ApiError.From(ErrorCodes.DeviceNotFound, $"Device '{deviceId}' was not found."))
                : Results.Ok(device);
        });

        group.MapGet("/{deviceId}/display", async (
            string deviceId,
            DeviceManager devices,
            CancellationToken cancellationToken) =>
        {
            var device = await devices.GetDeviceAsync(deviceId, cancellationToken);
            if (device is null)
            {
                return Results.NotFound(ApiError.From(ErrorCodes.DeviceNotFound, $"Device '{deviceId}' was not found."));
            }

            var display = await devices.GetDisplayAsync(deviceId, cancellationToken);

            return display is null
                ? Results.UnprocessableEntity(ApiError.From(
                    ErrorCodes.DisplayInfoFailed,
                    $"Could not read the screen size of '{device.DisplayName}'."))
                : Results.Ok(display);
        });

        group.MapGet("/{deviceId}/capabilities", async (
            string deviceId,
            DeviceManager devices,
            IWallpaperService wallpaper,
            CancellationToken cancellationToken) =>
        {
            var device = await devices.GetDeviceAsync(deviceId, cancellationToken);
            if (device is null)
            {
                return Results.NotFound(ApiError.From(ErrorCodes.DeviceNotFound, $"Device '{deviceId}' was not found."));
            }

            return Results.Ok(await wallpaper.GetCapabilitiesAsync(device, cancellationToken));
        });
    }
}
