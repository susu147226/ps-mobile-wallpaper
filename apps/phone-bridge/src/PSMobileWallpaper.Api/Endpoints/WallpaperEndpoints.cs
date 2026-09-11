using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Api.Realtime;
using PSMobileWallpaper.Application;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Api.Endpoints;

/// <summary>Spec §21/§22, wallpaper routes. Each operation publishes started/completed/failed events.</summary>
public static class WallpaperEndpoints
{
    public static void MapWallpaperEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallpaper").WithTags("Wallpaper");

        group.MapPost("/prepare", async (
            PrepareWallpaperRequest request,
            WallpaperWorkflow workflow,
            EventBroadcaster broadcaster,
            CancellationToken cancellationToken) =>
        {
            var prepared = await workflow.PrepareAsync(
                request.DeviceId, request.Path, request.Width, request.Height, cancellationToken);

            if (!prepared.Success)
            {
                return Results.UnprocessableEntity(
                    ApiError.From(prepared.ErrorCode ?? ErrorCodes.ImageProcessFailed, prepared.Message));
            }

            await broadcaster.PublishAsync(
                EventBroadcaster.Create(
                    BridgeEventNames.TransferCompleted,
                    new TransferEventPayload(request.DeviceId, "prepare", prepared.ImagePath, null)),
                cancellationToken);

            return Results.Ok(prepared);
        });

        group.MapPost("/send", (
            SendWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "send",
                ct => workflow.SendAsync(request.DeviceId, request.ImagePath, ct),
                cancellationToken));

        group.MapPost("/set-lock", (
            SetWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "lock",
                ct => workflow.SetLockAsync(request.DeviceId, request.ImagePath, ct),
                cancellationToken));

        group.MapPost("/set-home", (
            SetWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "home",
                ct => workflow.SetHomeAsync(request.DeviceId, request.ImagePath, ct),
                cancellationToken));

        group.MapPost("/set-both", (
            SetWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "both",
                ct => workflow.SetBothAsync(request.DeviceId, request.ImagePath, ct),
                cancellationToken));
    }

    private static async Task<IResult> RunAsync(
        string deviceId,
        string target,
        Func<CancellationToken, Task<WallpaperResult>> operation,
        CancellationToken cancellationToken)
    {
        WallpaperResult result;
        try
        {
            result = await operation(cancellationToken);
        }
        catch (Transport.Abstractions.TransportException ex)
        {
            result = WallpaperResult.Fail(deviceId, ex.ErrorCode, ex.Message);
        }

        return result.Success
            ? Results.Ok(result)
            : Results.UnprocessableEntity(ApiError.From(result.ErrorCode ?? ErrorCodes.WallpaperSetFailed, result.Message));
    }
}
