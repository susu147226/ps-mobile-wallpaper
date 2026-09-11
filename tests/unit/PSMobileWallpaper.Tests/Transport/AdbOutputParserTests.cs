using PSMobileWallpaper.Transport.Adb;

namespace PSMobileWallpaper.Tests.Transport;

/// <summary>Spec §5.2 / §6. Parser correctness decides what the plugin is told about the phone.</summary>
public sealed class AdbOutputParserTests
{
    private const string DevicesOutput = """
        List of devices attached
        R3CN30XXXX	device product:beyond1ltexx model:SM_G973F device:beyond1 transport_id:1
        1234567890	unauthorized
        emulator-5554	offline
        9ABCDEF	no permissions (user in plugdev group; are your udev rules wrong?); see [http://developer.android.com]
        """;

    [Fact]
    public void ParseDevices_ReadsEveryAttachedDevice()
    {
        var devices = AdbOutputParser.ParseDevices(DevicesOutput);

        Assert.Equal(4, devices.Count);
        Assert.Equal("R3CN30XXXX", devices[0].Id);
        Assert.Equal("1234567890", devices[1].Id);
        Assert.Equal("emulator-5554", devices[2].Id);
        Assert.Equal("9ABCDEF", devices[3].Id);
    }

    [Theory]
    [InlineData("device", "Connected")]
    [InlineData("unauthorized", "Unauthorized")]
    [InlineData("offline", "Offline")]
    [InlineData("no permissions (user in plugdev group)", "Unauthorized")]
    public void ParseDevices_MapsRawStateToUnifiedState(string rawState, string expected)
    {
        var devices = AdbOutputParser.ParseDevices($"List of devices attached\nABC\t{rawState}\n");

        Assert.Single(devices);
        Assert.Equal(expected, devices[0].State);
    }

    [Fact]
    public void ParseDevices_IgnoresHeaderBlankLinesAndJunk()
    {
        var devices = AdbOutputParser.ParseDevices("List of devices attached\n\n  \nlonely\n\nABC\tdevice\n");

        // "lonely" has no state column, so it is not a usable device row.
        Assert.Single(devices);
        Assert.Equal("ABC", devices[0].Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("List of devices attached")]
    public void ParseDevices_ReturnsEmptyForNoDevices(string? output)
    {
        Assert.Empty(AdbOutputParser.ParseDevices(output));
    }

    [Fact]
    public void ParseGetProp_ReadsBracketedFormat()
    {
        const string output = """
            [ro.product.brand]: [Xiaomi]
            [ro.product.manufacturer]: [Xiaomi]
            [ro.product.model]: [M2101K9G]
            [ro.build.version.release]: [13]
            [ro.build.version.sdk]: [33]
            """;

        var properties = AdbOutputParser.ParseGetProp(output);

        Assert.Equal("Xiaomi", properties["ro.product.brand"]);
        Assert.Equal("M2101K9G", properties["ro.product.model"]);
        Assert.Equal("13", properties["ro.build.version.release"]);
        Assert.Equal(5, properties.Count);
    }

    [Fact]
    public void ParseGetProp_HandlesEmptyValuesAndPlainColonFormat()
    {
        var properties = AdbOutputParser.ParseGetProp("[ro.foo]: []\nro.bar: baz\n");

        Assert.Equal(string.Empty, properties["ro.foo"]);
        Assert.Equal("baz", properties["ro.bar"]);
    }

    [Fact]
    public void ParseGetProp_IsCaseInsensitiveOnKeys()
    {
        var properties = AdbOutputParser.ParseGetProp("[RO.Product.Brand]: [Honor]\n");

        Assert.Equal("Honor", properties["ro.product.brand"]);
    }

    [Theory]
    [InlineData("Physical size: 1080x2400", 1080, 2400)]
    [InlineData("Physical size: 1220x2700", 1220, 2700)]
    [InlineData("Physical size: 1440x3120\nOverride size: 1080x2340", 1080, 2340)]
    public void ParseWmSize_PrefersOverrideOverPhysical(string output, int expectedWidth, int expectedHeight)
    {
        var (width, height) = AdbOutputParser.ParseWmSize(output);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage output")]
    [InlineData("Physical size: unknown")]
    public void ParseWmSize_ReturnsZeroWhenUnparseable(string? output)
    {
        var (width, height) = AdbOutputParser.ParseWmSize(output);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Theory]
    [InlineData("Physical density: 480", 480)]
    [InlineData("Physical density: 420\nOverride density: 400", 400)]
    [InlineData("Physical density: 320dpi", 320)]
    public void ParseWmDensity_ReadsDensity(string output, int expected)
    {
        Assert.Equal(expected, AdbOutputParser.ParseWmDensity(output));
    }

    [Fact]
    public void ParseRotation_ReadsSurfaceOrientation()
    {
        const string output = """
            Input Manager State:
              Display Viewports:
                SurfaceOrientation: 1
            """;

        Assert.Equal(1, AdbOutputParser.ParseRotation(output));
    }

    [Fact]
    public void ParseRotation_DefaultsToZeroWhenAbsent()
    {
        Assert.Equal(0, AdbOutputParser.ParseRotation("nothing useful here"));
    }
}
