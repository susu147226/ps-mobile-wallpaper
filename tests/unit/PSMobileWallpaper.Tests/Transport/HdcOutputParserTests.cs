using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.Tests.Transport;

/// <summary>Spec §5.3 / §6 / §16.</summary>
public sealed class HdcOutputParserTests
{
    [Fact]
    public void ParseTargets_ReadsBareKeysAsConnected()
    {
        var targets = HdcOutputParser.ParseTargets("7001005458323933323224223600\n");

        Assert.Single(targets);
        Assert.Equal("7001005458323933323224223600", targets[0].Id);
        Assert.Equal("Connected", targets[0].State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[Empty]")]
    public void ParseTargets_ReturnsEmptyWhenNoDevice(string output)
    {
        Assert.Empty(HdcOutputParser.ParseTargets(output));
    }

    [Fact]
    public void ParseTargets_ReadsVerboseStateColumn()
    {
        const string output = """
            7001005458323933323224223600	device
            127.0.0.1:5555	offline
            ABCD	unauthorized
            """;

        var targets = HdcOutputParser.ParseTargets(output);

        Assert.Equal(3, targets.Count);
        Assert.Equal("Connected", targets[0].State);
        Assert.Equal("Offline", targets[1].State);
        Assert.Equal("Unauthorized", targets[2].State);
    }

    [Fact]
    public void ParseTargets_ReadsTheRealVerboseColumnLayout()
    {
        // Actual `hdc list targets -v` output: key, transport, state, info, daemon — the state is
        // in column 3, and column 2 is the transport name, which is never a state.
        const string output = "COM1\t\tUART\tReady\tunknown...\thdc\r";

        var targets = HdcOutputParser.ParseTargets(output);

        Assert.Single(targets);
        Assert.Equal("COM1", targets[0].Id);
        Assert.Equal("Connected", targets[0].State);
    }

    [Fact]
    public void ParseTargets_TreatsTransportColumnAsNotAState()
    {
        // "UART" must never be mistaken for a state; without a recognised token the row is Unknown.
        var targets = HdcOutputParser.ParseTargets("COM3\tUART\n");

        Assert.Single(targets);
        Assert.Equal("Unknown", targets[0].State);
    }

    [Fact]
    public void ParseTargets_IgnoresBlankLines()
    {
        var targets = HdcOutputParser.ParseTargets("AAA\n\n   \nBBB\n");

        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void ParseParamGet_TrimsTrailingWhitespaceAndNewlines()
    {
        Assert.Equal("HUAWEI", HdcOutputParser.ParseParamGet("HUAWEI\n"));
        Assert.Equal(string.Empty, HdcOutputParser.ParseParamGet("  \n"));
        Assert.Equal(string.Empty, HdcOutputParser.ParseParamGet(null));
    }

    [Fact]
    public void ParseScreenSize_ReadsTheRealHidumperDump()
    {
        // Verbatim excerpt from `hdc shell hidumper -s RenderService -a screen` on a HarmonyOS handset.
        const string output = """
            ----------------------------------RenderService----------------------------------
            -- ScreenInfo
            screen[0]: id=0, powerStatus=POWER_STATUS_ON, backlight=16920, screenType=EXTERNAL_TYPE, render resolution=1152x2520, physical resolution=1280x2800, isVirtual=false, skipFrameInterval=1, expectedRefreshRate=-1, skipFrameStrategy=0
            supportedMode[0]: 1280x2800, refreshRate=60
            supportedMode[1]: 1280x2800, refreshRate=90
            supportedMode[2]: 1280x2800, refreshRate=120
            activeMode: 1280x2800, refreshRate=60
            name=, phyWidth=74, phyHeight=158, supportLayers=12, virtualDispCount=0, propertyCount=3, type=DISP_INTF_UNKNOW, supportWriteBack=false
            """;

        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        // Physical panel size wins over the lower render resolution.
        Assert.Equal(1280, width);
        Assert.Equal(2800, height);
    }

    [Fact]
    public void ParseScreenSize_FallsBackToRenderResolutionWhenPhysicalIsAbsent()
    {
        const string output = "screen[0]: render resolution=1152x2520, isVirtual=false\n";

        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.Equal(1152, width);
        Assert.Equal(2520, height);
    }

    [Fact]
    public void ParseScreenSize_FallsBackToActiveModeWhenNoResolutionFieldsExist()
    {
        const string output = "activeMode: 1220x2700, refreshRate=90\n";

        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.Equal(1220, width);
        Assert.Equal(2700, height);
    }

    [Fact]
    public void ParseScreenSize_IgnoresRefreshRatesAndOtherNumbers()
    {
        // refreshRate=60 and phyWidth/phyHeight must not be mistaken for a resolution.
        const string output = "activeMode: 1280x2800, refreshRate=60\nname=, phyWidth=74, phyHeight=158\n";

        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.Equal(1280, width);
        Assert.Equal(2800, height);
    }

    [Fact]
    public void ParseScreenSize_ReadsActiveMode()
    {
        const string output = """
            RenderService:
              ScreenInfo:
                activeMode: 1220x2700
                supportModes: 1220x2700 120
            """;

        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.Equal(1220, width);
        Assert.Equal(2700, height);
    }

    [Fact]
    public void ParseScreenSize_IgnoresTrailingRefreshRate()
    {
        var (width, height) = HdcOutputParser.ParseScreenSize("activeMode: 1260x2720 120\n");

        Assert.Equal(1260, width);
        Assert.Equal(2720, height);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no screen info here")]
    [InlineData("activeMode: unknown")]
    public void ParseScreenSize_ReturnsZeroWhenUnparseable(string output)
    {
        var (width, height) = HdcOutputParser.ParseScreenSize(output);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void ParseDensity_ConvertsScaleFactorToAndroidDensityDpi()
    {
        // Verbatim excerpt from `hdc shell hidumper -s DisplayManagerService -a -a`.
        const string output = """
            ----------------------------------DisplayManagerService----------------------------------
            Density:                      3.15
            DensityInCurResolution:       3.15
            DPI<X, Y>:                    395.416, 405.113
            """;

        // 3.15 x 160 = 504, matching the densityDpi units `adb shell wm density` reports.
        Assert.Equal(504, HdcOutputParser.ParseDensity(output));
    }

    [Theory]
    [InlineData("Density: 2.0", 320)]
    [InlineData("Density: 1.5", 240)]
    [InlineData("Density: 3", 480)]
    [InlineData("density: 2.75", 440)]
    public void ParseDensity_ScalesToDensityDpi(string output, int expected)
    {
        Assert.Equal(expected, HdcOutputParser.ParseDensity(output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DPI<X, Y>:  395.416, 405.113")]
    [InlineData("Density: 0.5")]
    [InlineData("Density: unknown")]
    public void ParseDensity_ReturnsZeroWhenNotReported(string? output)
    {
        Assert.Equal(0, HdcOutputParser.ParseDensity(output));
    }

    [Fact]
    public void ParseDensity_DoesNotConfuseDensityInCurResolution()
    {
        // Only the plain `Density` key is authoritative; the sibling key must not win by position.
        const string output = "DensityInCurResolution:       9.99\nDensity:                      2.0\n";

        Assert.Equal(320, HdcOutputParser.ParseDensity(output));
    }
}
