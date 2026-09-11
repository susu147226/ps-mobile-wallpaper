using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Image.Abstractions;
using SkiaSharp;

namespace PSMobileWallpaper.Image;

/// <summary>
/// Spec §2.5 / §12. SkiaSharp-backed implementation. Every write lands in the temp workspace
/// described by spec §8: %TEMP%/PSMobileWallpaper/wallpaper_{timestamp}.{ext}.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private static readonly SKSamplingOptions Sampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly ILogger<SkiaImageProcessor> _logger;
    private readonly string _outputDirectory;
    private readonly ImageFormat _format;
    private readonly int _quality;

    public SkiaImageProcessor(
        ILogger<SkiaImageProcessor> logger,
        string? outputDirectory = null,
        ImageFormat format = ImageFormat.Png,
        int quality = 95)
    {
        _logger = logger;
        _format = format;
        _quality = Math.Clamp(quality, 1, 100);
        _outputDirectory = outputDirectory
            ?? Path.Combine(Path.GetTempPath(), "PSMobileWallpaper");

        Directory.CreateDirectory(_outputDirectory);
    }

    public Task<ImageInfo> GetInfoAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image not found: {imagePath}", imagePath);
        }

        using var codec = SKCodec.Create(imagePath)
            ?? throw new InvalidOperationException($"{ErrorCodes.ImageProcessFailed}: unable to decode '{imagePath}'.");

        return Task.FromResult(new ImageInfo
        {
            Path = imagePath,
            Width = codec.Info.Width,
            Height = codec.Info.Height,
            Format = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant(),
            SizeBytes = new FileInfo(imagePath).Length,
        });
    }

    public Task<string> CenterCropAsync(
        string imagePath,
        int width,
        int height,
        CancellationToken cancellationToken = default) =>
        CropAsync(imagePath, width, height, CropMode.CenterCrop, customRegion: null, cancellationToken);

    public Task<string> CropAsync(
        string imagePath,
        int width,
        int height,
        CropMode mode,
        CropRect? customRegion = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = Decode(imagePath);
        var plan = CropCalculator.Compute(mode, source.Width, source.Height, width, height, customRegion);

        _logger.LogInformation(
            "Applying {Mode} to {SourceWidth}x{SourceHeight} -> {TargetWidth}x{TargetHeight}: " +
            "source ({SourceX},{SourceY}) {SourceW}x{SourceH} into ({DestX},{DestY}) {DestW}x{DestH}.",
            mode, source.Width, source.Height, width, height,
            Math.Round(plan.Source.X), Math.Round(plan.Source.Y),
            Math.Round(plan.Source.Width), Math.Round(plan.Source.Height),
            Math.Round(plan.Destination.X), Math.Round(plan.Destination.Y),
            Math.Round(plan.Destination.Width), Math.Round(plan.Destination.Height));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var result = new SKBitmap(info);

        using (var canvas = new SKCanvas(result))
        {
            // Only "fit" leaves part of the target uncovered, so only it needs a defined backdrop.
            if (mode == CropMode.CenterFit)
            {
                canvas.Clear(_format == ImageFormat.Jpeg ? SKColors.Black : SKColors.Transparent);
            }

            canvas.DrawBitmap(source, ToSkRect(plan.Source), ToSkRect(plan.Destination), Sampling, paint: null);
            canvas.Flush();
        }

        return Task.FromResult(Save(result));
    }

    public Task<string> ResizeAsync(
        string imagePath,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = Decode(imagePath);
        using var result = source.Resize(new SKImageInfo(width, height), Sampling)
            ?? throw new InvalidOperationException($"{ErrorCodes.ImageProcessFailed}: resize to {width}x{height} failed.");

        return Task.FromResult(Save(result));
    }

    private static SKBitmap Decode(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image not found: {imagePath}", imagePath);
        }

        return SKBitmap.Decode(imagePath)
            ?? throw new InvalidOperationException($"{ErrorCodes.ImageProcessFailed}: unable to decode '{imagePath}'.");
    }

    private static SKRect ToSkRect(CropRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private string Save(SKBitmap bitmap)
    {
        var extension = _format == ImageFormat.Jpeg ? "jpg" : "png";
        var path = Path.Combine(
            _outputDirectory,
            $"wallpaper_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.{extension}");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = _format == ImageFormat.Jpeg
            ? image.Encode(SKEncodedImageFormat.Jpeg, _quality)
            : image.Encode(SKEncodedImageFormat.Png, 100);

        if (data is null)
        {
            throw new InvalidOperationException($"{ErrorCodes.ImageProcessFailed}: encoding to {extension} failed.");
        }

        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);

        _logger.LogInformation("Wrote processed image to '{Path}'.", path);
        return path;
    }
}
