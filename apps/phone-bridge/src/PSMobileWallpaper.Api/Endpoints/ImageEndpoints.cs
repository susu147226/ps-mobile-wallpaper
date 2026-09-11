using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Application;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Image.Abstractions;

namespace PSMobileWallpaper.Api.Endpoints;

/// <summary>Spec §21, image routes.</summary>
public static class ImageEndpoints
{
    public static void MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/images").WithTags("Images");

        group.MapPost("/", async (
            ImageRequest request,
            IImageProcessor processor,
            CancellationToken cancellationToken) =>
        {
            if (!File.Exists(request.Path))
            {
                return Results.NotFound(ApiError.From(ErrorCodes.ImageNotFound, $"Image not found: {request.Path}"));
            }

            try
            {
                return Results.Ok(await processor.GetInfoAsync(request.Path, cancellationToken));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                return Results.UnprocessableEntity(ApiError.From(ErrorCodes.ImageProcessFailed, ex.Message));
            }
        });

        group.MapPost("/crop", async (
            CropRequest request,
            WallpaperWorkflow workflow,
            CancellationToken cancellationToken) =>
        {
            var prepared = await workflow.PrepareAsync(
                request.DeviceId, request.Path, request.Width, request.Height, cancellationToken);

            return prepared.Success
                ? Results.Ok(prepared)
                : Results.UnprocessableEntity(ApiError.From(prepared.ErrorCode ?? ErrorCodes.ImageProcessFailed, prepared.Message));
        });
    }
}
