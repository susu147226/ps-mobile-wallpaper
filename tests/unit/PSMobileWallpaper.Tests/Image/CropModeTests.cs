using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Image;

namespace PSMobileWallpaper.Tests.Image;

/// <summary>Spec §11. Geometry for every crop mode, independent of any imaging library.</summary>
public sealed class CropModeTests
{
    private const int SourceWidth = 4000;
    private const int SourceHeight = 3000;
    private const int TargetWidth = 1228;
    private const int TargetHeight = 2700;

    private static CropPlan Plan(CropMode mode, CropRect? region = null) =>
        CropCalculator.Compute(mode, SourceWidth, SourceHeight, TargetWidth, TargetHeight, region);

    [Fact]
    public void CenterCrop_FillsTheTargetWithACentredRegion()
    {
        var plan = Plan(CropMode.CenterCrop);

        Assert.Equal(new CropRect(0, 0, TargetWidth, TargetHeight), plan.Destination);
        Assert.Equal(CropCalculator.ComputeCenterCrop(SourceWidth, SourceHeight, TargetWidth, TargetHeight), plan.Source);
    }

    [Fact]
    public void CenterFit_KeepsTheWholeSourceAndInsetsTheDestination()
    {
        var plan = Plan(CropMode.CenterFit);

        // Nothing is cropped: the entire source is drawn.
        Assert.Equal(new CropRect(0, 0, SourceWidth, SourceHeight), plan.Source);

        // 4000x3000 (4:3) inside 1228x2700: height is the binding constraint.
        Assert.Equal(1228 * 3000d / 4000d, plan.Destination.Height, 6);
        Assert.Equal(1228, plan.Destination.Width, 6);
        Assert.Equal((TargetHeight - plan.Destination.Height) / 2d, plan.Destination.Y, 6);
        Assert.Equal(0, plan.Destination.X, 6);
    }

    [Fact]
    public void CenterFit_PreservesTheSourceAspectRatio()
    {
        var plan = Plan(CropMode.CenterFit);

        var destinationRatio = plan.Destination.Width / plan.Destination.Height;
        var sourceRatio = (double)SourceWidth / SourceHeight;

        Assert.Equal(sourceRatio, destinationRatio, 6);
    }

    [Fact]
    public void CenterFit_LetterboxesVerticallyForAWideSource()
    {
        var plan = CropCalculator.Compute(CropMode.CenterFit, 4000, 3000, 1228, 2700);

        // Bars appear only on the axis the image does not fill.
        Assert.True(plan.Destination.Y > 0, "expected vertical letterbox bars");
        Assert.Equal(0, plan.Destination.X, 6);
    }

    [Fact]
    public void CenterFit_PillarboxesForATallSource()
    {
        var plan = CropCalculator.Compute(CropMode.CenterFit, 1000, 4000, 1228, 2700);

        Assert.True(plan.Destination.X > 0, "expected horizontal pillarbox bars");
        Assert.Equal(0, plan.Destination.Y, 6);
    }

    [Fact]
    public void Stretch_MapsTheWholeSourceOntoTheWholeTarget()
    {
        var plan = Plan(CropMode.Stretch);

        Assert.Equal(new CropRect(0, 0, SourceWidth, SourceHeight), plan.Source);
        Assert.Equal(new CropRect(0, 0, TargetWidth, TargetHeight), plan.Destination);
    }

    [Fact]
    public void TopCrop_AnchorsToTheTopEdgeAndKeepsTheCenterCropExtent()
    {
        var top = Plan(CropMode.TopCrop);
        var center = Plan(CropMode.CenterCrop);

        Assert.Equal(0, top.Source.Y, 6);
        Assert.Equal(center.Source.Width, top.Source.Width, 6);
        Assert.Equal(center.Source.Height, top.Source.Height, 6);
        Assert.Equal(center.Source.X, top.Source.X, 6); // horizontal axis stays centred
        Assert.Equal(new CropRect(0, 0, TargetWidth, TargetHeight), top.Destination);
    }

    [Fact]
    public void BottomCrop_AnchorsToTheBottomEdgeAndKeepsTheCenterCropExtent()
    {
        var bottom = Plan(CropMode.BottomCrop);
        var center = Plan(CropMode.CenterCrop);

        Assert.Equal(SourceHeight - center.Source.Height, bottom.Source.Y, 6);
        Assert.Equal(center.Source.Width, bottom.Source.Width, 6);
        Assert.Equal(center.Source.Height, bottom.Source.Height, 6);
        Assert.Equal(center.Source.X, bottom.Source.X, 6);
        Assert.Equal(new CropRect(0, 0, TargetWidth, TargetHeight), bottom.Destination);
    }

    [Fact]
    public void TopAndBottomCrop_SitAtOppositeEdgesOfTheSameRegion()
    {
        var top = Plan(CropMode.TopCrop);
        var bottom = Plan(CropMode.BottomCrop);

        Assert.Equal(0, top.Source.Y, 6);
        Assert.Equal(SourceHeight, bottom.Source.Y + bottom.Source.Height, 6);
    }

    [Fact]
    public void Custom_UsesExactlyTheRequestedRegion()
    {
        var region = new CropRect(100, 250, 800, 1600);

        var plan = Plan(CropMode.Custom, region);

        Assert.Equal(region, plan.Source);
        Assert.Equal(new CropRect(0, 0, TargetWidth, TargetHeight), plan.Destination);
    }

    [Fact]
    public void Custom_WithoutARegion_Throws()
    {
        Assert.Throws<ArgumentException>(() => Plan(CropMode.Custom));
    }

    [Theory]
    [InlineData(0, 0, 0, 100)]      // zero width
    [InlineData(0, 0, 100, 0)]      // zero height
    [InlineData(-1, 0, 100, 100)]   // negative x
    [InlineData(0, -1, 100, 100)]   // negative y
    [InlineData(3900, 0, 200, 100)] // overflows the right edge
    [InlineData(0, 2900, 100, 200)] // overflows the bottom edge
    public void Custom_RejectsRegionsOutsideTheSource(double x, double y, double width, double height)
    {
        var region = new CropRect(x, y, width, height);

        Assert.Throws<ArgumentException>(() => Plan(CropMode.Custom, region));
    }

    [Fact]
    public void Custom_AcceptsARegionFlushWithTheEdges()
    {
        var region = new CropRect(SourceWidth - 100, SourceHeight - 200, 100, 200);

        var plan = Plan(CropMode.Custom, region);

        Assert.Equal(region, plan.Source);
    }

    [Theory]
    [InlineData(CropMode.CenterCrop)]
    [InlineData(CropMode.CenterFit)]
    [InlineData(CropMode.Stretch)]
    [InlineData(CropMode.TopCrop)]
    [InlineData(CropMode.BottomCrop)]
    public void EveryMode_KeepsSourceAndDestinationInsideTheirBounds(CropMode mode)
    {
        (int W, int H)[] sources = [(4000, 3000), (1000, 4000), (640, 480), (1228, 2700)];
        (int W, int H)[] targets = [(1228, 2700), (512, 512), (2560, 1440), (1080, 1920)];

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                var plan = CropCalculator.Compute(mode, source.W, source.H, target.W, target.H);

                Assert.True(plan.Source.X >= -1e-6, $"{mode}: source x escaped for {source} -> {target}");
                Assert.True(plan.Source.Y >= -1e-6, $"{mode}: source y escaped for {source} -> {target}");
                Assert.True(plan.Source.X + plan.Source.Width <= source.W + 1e-6, $"{mode}: source right escaped");
                Assert.True(plan.Source.Y + plan.Source.Height <= source.H + 1e-6, $"{mode}: source bottom escaped");

                Assert.True(plan.Destination.X >= -1e-6, $"{mode}: dest x escaped");
                Assert.True(plan.Destination.Y >= -1e-6, $"{mode}: dest y escaped");
                Assert.True(plan.Destination.X + plan.Destination.Width <= target.W + 1e-6, $"{mode}: dest right escaped");
                Assert.True(plan.Destination.Y + plan.Destination.Height <= target.H + 1e-6, $"{mode}: dest bottom escaped");

                Assert.True(plan.Source.Width > 0 && plan.Source.Height > 0);
                Assert.True(plan.Destination.Width > 0 && plan.Destination.Height > 0);
            }
        }
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(-100, 100, 100, 100)]
    public void EveryMode_RejectsNonPositiveDimensions(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        foreach (var mode in Enum.GetValues<CropMode>())
        {
            var region = new CropRect(0, 0, 10, 10);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CropCalculator.Compute(mode, sourceWidth, sourceHeight, targetWidth, targetHeight, region));
        }
    }

    [Fact]
    public void CropModes_RoundTripConfigNames()
    {
        foreach (var mode in Enum.GetValues<CropMode>())
        {
            var name = CropModes.ToConfigName(mode);

            Assert.Equal(mode, CropModes.Parse(name));
        }
    }

    [Theory]
    [InlineData(null, CropMode.CenterCrop)]
    [InlineData("", CropMode.CenterCrop)]
    [InlineData("nonsense", CropMode.CenterCrop)]
    [InlineData("CENTER-CROP", CropMode.CenterCrop)]
    [InlineData("center-fit", CropMode.CenterFit)]
    [InlineData("bottom-crop", CropMode.BottomCrop)]
    public void CropModes_ParseFallsBackToCenterCrop(string? value, CropMode expected)
    {
        Assert.Equal(expected, CropModes.Parse(value));
    }
}
