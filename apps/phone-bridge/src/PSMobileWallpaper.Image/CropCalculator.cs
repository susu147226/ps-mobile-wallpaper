using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Image;

/// <summary>A region of an image, in pixels.</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height);

/// <summary>
/// Where to read from and where to draw to. One shape covers every mode in spec §11:
/// cropping modes inset the source and fill the destination, while "fit" keeps the whole source
/// and insets the destination.
/// </summary>
public readonly record struct CropPlan(CropRect Source, CropRect Destination);

/// <summary>
/// Spec §10 / §11. Pure crop geometry, kept free of any imaging dependency so the ratio maths can be
/// unit-tested directly (spec §40).
/// </summary>
public static class CropCalculator
{
    /// <summary>Computes the plan for any supported crop mode.</summary>
    /// <param name="mode">Spec §11 crop mode.</param>
    /// <param name="customRegion">
    /// Required for <see cref="CropMode.Custom"/>: the source region to take, in source pixels.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any dimension is not positive.</exception>
    /// <exception cref="ArgumentException">A custom mode was requested without a usable region.</exception>
    public static CropPlan Compute(
        CropMode mode,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        CropRect? customRegion = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var fullSource = new CropRect(0, 0, sourceWidth, sourceHeight);
        var fullDestination = new CropRect(0, 0, targetWidth, targetHeight);

        return mode switch
        {
            CropMode.CenterCrop => new CropPlan(
                ComputeCenterCrop(sourceWidth, sourceHeight, targetWidth, targetHeight),
                fullDestination),

            CropMode.TopCrop => new CropPlan(
                ComputeEdgeCrop(sourceWidth, sourceHeight, targetWidth, targetHeight, fromTop: true),
                fullDestination),

            CropMode.BottomCrop => new CropPlan(
                ComputeEdgeCrop(sourceWidth, sourceHeight, targetWidth, targetHeight, fromTop: false),
                fullDestination),

            // Stretch ignores the aspect ratio entirely: the whole source is drawn into the whole target.
            CropMode.Stretch => new CropPlan(fullSource, fullDestination),

            CropMode.CenterFit => new CropPlan(
                fullSource,
                ComputeContainDestination(sourceWidth, sourceHeight, targetWidth, targetHeight)),

            CropMode.Custom => new CropPlan(
                ValidateCustomRegion(customRegion, sourceWidth, sourceHeight),
                fullDestination),

            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported crop mode."),
        };
    }

    /// <summary>
    /// Spec §10. The largest source region matching the target aspect ratio, centred on the source.
    /// The region lies fully inside the source, so the target is covered without letterboxing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Any dimension is not positive.</exception>
    public static CropRect ComputeCenterCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var (width, height) = ComputeCropExtent(sourceWidth, sourceHeight, targetWidth, targetHeight);

        return new CropRect((sourceWidth - width) / 2d, (sourceHeight - height) / 2d, width, height);
    }

    /// <summary>
    /// Same region size as center-crop, but anchored to the top or bottom edge. The horizontal axis
    /// stays centred, since only the vertical anchor is meaningful here (spec §11).
    /// </summary>
    private static CropRect ComputeEdgeCrop(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool fromTop)
    {
        var (width, height) = ComputeCropExtent(sourceWidth, sourceHeight, targetWidth, targetHeight);
        var y = fromTop ? 0d : sourceHeight - height;

        return new CropRect((sourceWidth - width) / 2d, y, width, height);
    }

    /// <summary>The largest extent with the target aspect ratio that fits inside the source.</summary>
    private static (double Width, double Height) ComputeCropExtent(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var sourceRatio = (double)sourceWidth / sourceHeight;
        var targetRatio = (double)targetWidth / targetHeight;

        return sourceRatio > targetRatio
            // Source is relatively wider: keep full height, trim the sides.
            ? (sourceHeight * targetRatio, sourceHeight)
            // Source is relatively taller (or equal): keep full width, trim top and bottom.
            : (sourceWidth, sourceWidth / targetRatio);
    }

    /// <summary>Scales the whole source down to fit inside the target, centred — the letterbox side of §11.</summary>
    private static CropRect ComputeContainDestination(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var scale = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;

        return new CropRect((targetWidth - width) / 2d, (targetHeight - height) / 2d, width, height);
    }

    private static CropRect ValidateCustomRegion(CropRect? region, int sourceWidth, int sourceHeight)
    {
        if (region is not { } custom)
        {
            throw new ArgumentException(
                "CropMode.Custom requires a region.",
                nameof(region));
        }

        if (custom.Width <= 0 || custom.Height <= 0)
        {
            throw new ArgumentException(
                $"A custom region must have a positive size, but was {custom.Width}x{custom.Height}.",
                nameof(region));
        }

        if (custom.X < 0 || custom.Y < 0 ||
            custom.X + custom.Width > sourceWidth ||
            custom.Y + custom.Height > sourceHeight)
        {
            throw new ArgumentException(
                $"A custom region must lie inside the {sourceWidth}x{sourceHeight} source, " +
                $"but was ({custom.X},{custom.Y}) {custom.Width}x{custom.Height}.",
                nameof(region));
        }

        return custom;
    }
}
