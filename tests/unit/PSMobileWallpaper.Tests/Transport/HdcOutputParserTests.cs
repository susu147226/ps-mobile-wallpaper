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
}
