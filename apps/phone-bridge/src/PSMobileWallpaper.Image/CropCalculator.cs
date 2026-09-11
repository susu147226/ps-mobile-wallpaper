namespace PSMobileWallpaper.Image;

/// <summary>A source-image region, in source pixels.</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height);

/// <summary>
/// Spec §10. Pure center-crop geometry, kept free of any imaging dependency so the ratio maths
/// can be unit-tested directly (spec §40).
/// </summary>
public static class CropCalculator
{
    /// <summary>
    /// Computes the largest source region that matches the target aspect ratio and is centred on
    /// the source. The region is fully inside the source, so the target is covered without
    /// letterboxing once scaled up.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Any dimension is not positive.</exception>
    public static CropRect ComputeCenterCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var sourceRatio = (double)sourceWidth / sourceHeight;
        var targetRatio = (double)targetWidth / targetHeight;

        double cropWidth;
        double cropHeight;

        if (sourceRatio > targetRatio)
        {
            // Source is relatively wider: keep full height, trim the sides.
            cropHeight = sourceHeight;
            cropWidth = sourceHeight * targetRatio;
        }
        else
        {
            // Source is relatively taller (or equal): keep full width, trim top and bottom.
            cropWidth = sourceWidth;
            cropHeight = sourceWidth / targetRatio;
        }

        var x = (sourceWidth - cropWidth) / 2d;
        var y = (sourceHeight - cropHeight) / 2d;

        return new CropRect(x, y, cropWidth, cropHeight);
    }
}
