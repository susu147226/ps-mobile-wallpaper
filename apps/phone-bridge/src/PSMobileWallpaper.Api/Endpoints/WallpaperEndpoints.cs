using Microsoft.Extensions.Options;
using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Api.Realtime;
using PSMobileWallpaper.Application;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Infrastructure.Configuration;

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
            PreparedImageStore preparedImages,
            IOptions<ImageOptions> imageOptions,
            CancellationToken cancellationToken) =>
        {
            var mode = CropRequestExtensions.ResolveMode(
                request.Mode,
                CropModes.Parse(imageOptions.Value.CropMode));

            var prepared = await workflow.PrepareAsync(
                request.DeviceId,
                request.Path,
                request.Width,
                request.Height,
                mode,
                request.Region.ToCropRect(),
                cancellationToken);

            if (!prepared.Success)
            {
                return Results.UnprocessableEntity(
                    ApiError.From(prepared.ErrorCode ?? ErrorCodes.ImageProcessFailed, prepared.Message));
            }

            // Keep it so a phone-side helper can download it over the reverse port forward.
            if (!string.IsNullOrEmpty(prepared.ImagePath))
            {
                preparedImages.Set(prepared.ImagePath);
            }

            await broadcaster.PublishAsync(
                EventBroadcaster.Create(
                    BridgeEventNames.TransferCompleted,
                    new TransferEventPayload(request.DeviceId, "prepare", prepared.ImagePath, null)),
                cancellationToken);

            return Results.Ok(prepared);
        });

        // Served for the phone-side helper app. On both tested platforms the bridge cannot place a
        // file into the gallery itself, so the app downloads the bytes and the user saves them.
        group.MapGet("/latest-image", (PreparedImageStore preparedImages) =>
        {
            var path = preparedImages.Get();

            return path is null
                ? Results.NotFound(ApiError.From(
                    ErrorCodes.ImageNotFound,
                    "No wallpaper has been prepared yet. Run 'preview crop' in the panel first."))
                : Results.File(path, contentType: "image/png");
        });

        group.MapPost("/save-to-gallery", (
            SendWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "gallery",
                ct => workflow.SaveToGalleryAsync(request.DeviceId, request.ImagePath, ct),
                cancellationToken));

        group.MapPost("/send", (
            SendWallpaperRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
            RunAsync(
                request.DeviceId,
                "gallery",
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
