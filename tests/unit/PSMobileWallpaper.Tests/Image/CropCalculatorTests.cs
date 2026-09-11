using PSMobileWallpaper.Image;

namespace PSMobileWallpaper.Tests.Image;

/// <summary>Spec §10. The center-crop maths is the core algorithm of the whole tool.</summary>
public sealed class CropCalculatorTests
{
    [Fact]
    public void SameAspectRatio_KeepsEntireSource()
    {
        var crop = CropCalculator.ComputeCenterCrop(1080, 1920, 270, 480);

        Assert.Equal(0, crop.X, 3);
        Assert.Equal(0, crop.Y, 3);
        Assert.Equal(1080, crop.Width, 3);
        Assert.Equal(1920, crop.Height, 3);
    }

    [Fact]
    public void WiderSource_TrimsSidesAndKeepsFullHeight()
    {
        // 4000x2000 (2:1) into a 9:16 phone screen (0.5625:1).
        var crop = CropCalculator.ComputeCenterCrop(4000, 2000, 1080, 1920);

        Assert.Equal(2000, crop.Height, 3);          // full height kept
        Assert.Equal(2000 * 1080d / 1920d, crop.Width, 3); // width follows the target ratio
        Assert.Equal(1125, crop.Width, 3);
        Assert.Equal((4000 - 1125) / 2d, crop.X, 3); // horizontally centred
        Assert.Equal(0, crop.Y, 3);
    }

    [Fact]
    public void TallerSource_TrimsTopAndBottomAndKeepsFullWidth()
    {
        // A tall poster (1:2) into a wider tablet screen (16:10).
        var crop = CropCalculator.ComputeCenterCrop(1000, 2000, 1600, 1000);

        Assert.Equal(1000, crop.Width, 3);
        Assert.Equal(1000 * 1000d / 1600d, crop.Height, 3);
        Assert.Equal(625, crop.Height, 3);
        Assert.Equal(0, crop.X, 3);
        Assert.Equal((2000 - 625) / 2d, crop.Y, 3);
    }

    [Fact]
    public void CropRegion_AlwaysMatchesTargetAspectRatio()
    {
        (int W, int H)[] sources = [(4000, 2000), (1000, 2000), (1080, 1920), (7680, 4320), (640, 480)];
        (int W, int H)[] targets = [(1080, 1920), (1220, 2700), (1440, 2560), (1600, 1000), (512, 512)];

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                var crop = CropCalculator.ComputeCenterCrop(source.W, source.H, target.W, target.H);

                var cropRatio = crop.Width / crop.Height;
                var targetRatio = (double)target.W / target.H;

                Assert.Equal(targetRatio, cropRatio, 6);
            }
        }
    }

    [Fact]
    public void CropRegion_NeverEscapesTheSourceImage()
    {
        (int W, int H)[] sources = [(4000, 2000), (10, 9999), (9999, 10), (1, 1), (1234, 5678)];
        (int W, int H)[] targets = [(1080, 1920), (1, 1), (9999, 1234), (1234, 9999)];

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                var crop = CropCalculator.ComputeCenterCrop(source.W, source.H, target.W, target.H);

                Assert.True(crop.X >= -1e-9, $"x={crop.X} for {source} -> {target}");
                Assert.True(crop.Y >= -1e-9, $"y={crop.Y} for {source} -> {target}");
                Assert.True(crop.X + crop.Width <= source.W + 1e-6, $"right edge escaped for {source} -> {target}");
                Assert.True(crop.Y + crop.Height <= source.H + 1e-6, $"bottom edge escaped for {source} -> {target}");
                Assert.True(crop.Width > 0);
                Assert.True(crop.Height > 0);
            }
        }
    }

    [Fact]
    public void CropRegion_IsMaximalForTheTargetRatio()
    {
        // The region must be the largest one with the target ratio that still fits inside the source,
        // which means it touches the source bounds on at least one axis.
        (int W, int H)[] sources = [(4000, 2000), (1000, 2000), (1080, 1920), (640, 480), (3333, 777)];
        (int W, int H)[] targets = [(1080, 1920), (1220, 2700), (1600, 1000), (512, 512)];

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                var crop = CropCalculator.ComputeCenterCrop(source.W, source.H, target.W, target.H);

                var spansWidth = Math.Abs(crop.Width - source.W) < 1e-6;
                var spansHeight = Math.Abs(crop.Height - source.H) < 1e-6;

                Assert.True(
                    spansWidth || spansHeight,
                    $"Crop {crop.Width}x{crop.Height} is not maximal for {source} -> {target}");
            }
        }
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(-1, 100, 100, 100)]
    [InlineData(100, 100, -5, 100)]
    public void NonPositiveDimensions_Throw(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CropCalculator.ComputeCenterCrop(sourceWidth, sourceHeight, targetWidth, targetHeight));
    }

    [Fact]
    public void RealisticPhoneCrop_ProducesExpectedRegion()
    {
        // 6000x4000 camera shot into a 1220x2700 Huawei screen.
        var crop = CropCalculator.ComputeCenterCrop(6000, 4000, 1220, 2700);

        Assert.Equal(4000, crop.Height, 3);
        Assert.Equal(4000 * 1220d / 2700d, crop.Width, 3);
        Assert.Equal((6000 - crop.Width) / 2d, crop.X, 3);
        Assert.Equal(0, crop.Y, 3);
    }
}
