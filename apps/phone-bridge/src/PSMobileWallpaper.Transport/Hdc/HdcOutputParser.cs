using System.Globalization;
using System.Text.RegularExpressions;

namespace PSMobileWallpaper.Transport.Hdc;

/// <summary>One row of `hdc list targets`, before brand/model enrichment.</summary>
public sealed record HdcRawTarget(string Id, string State);

/// <summary>
/// Pure parsers for hdc output (spec §16), split out so they can be unit-tested without a device.
/// </summary>
public static class HdcOutputParser
{
    /// <summary>
    /// Label-anchored resolution patterns. Anchoring matters: a single RenderService line can carry
    /// both `render resolution=1152x2520` and `physical resolution=1280x2800`, so taking the first
    /// `WxH` on the line would pick the wrong one.
    /// </summary>
    private static readonly Regex PhysicalResolutionPattern = new(
        @"physical\s+resolution\s*[=:]\s*(\d{2,5})\s*[xX]\s*(\d{2,5})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RenderResolutionPattern = new(
        @"render\s+resolution\s*[=:]\s*(\d{2,5})\s*[xX]\s*(\d{2,5})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ActiveModePattern = new(
        @"activeMode\s*[=:]\s*(\d{2,5})\s*[xX]\s*(\d{2,5})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Recognised connection states. `hdc list targets -v` words the column as "Ready";
    /// the plain listing omits it entirely.
    /// </summary>
    private static readonly (string Key, string Mapped)[] StateMap =
    [
        ("unauthorized", "Unauthorized"),
        ("offline", "Offline"),
        ("ready", "Connected"),
        ("connected", "Connected"),
        ("online", "Connected"),
        ("device", "Connected"),
    ];

    /// <summary>
    /// Parses `hdc list targets` / `hdc list targets -v`.
    /// The plain form is one connect key per line; the verbose form is
    /// <c>key  transport  state  info  daemon</c>, so the state is searched for rather than
    /// assumed to sit in a fixed column. The literal token `[Empty]` means no device is attached.
    /// </summary>
    public static IReadOnlyList<HdcRawTarget> ParseTargets(string? output)
    {
        var targets = new List<HdcRawTarget>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return targets;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.Equals("[Empty]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Empty", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length == 0)
            {
                continue;
            }

            // A bare key line carries no state; hdc only lists reachable targets by default.
            var state = columns.Length < 2 ? "Connected" : DetectState(columns);

            targets.Add(new HdcRawTarget(columns[0], state));
        }

        return targets;
    }

    private static string DetectState(string[] columns)
    {
        // The transport column ("UART", "USB", "TCP") is never a state, so scan for a known token.
        foreach (var column in columns.Skip(1))
        {
            var match = StateMap.FirstOrDefault(
                candidate => column.Equals(candidate.Key, StringComparison.OrdinalIgnoreCase));

            if (match.Mapped is not null)
            {
                return match.Mapped;
            }
        }

        return "Unknown";
    }

    /// <summary>Parses `hdc shell param get &lt;key&gt;`, which prints the bare value.</summary>
    public static string ParseParamGet(string? output) => output?.Trim() ?? string.Empty;

    /// <summary>
    /// Parses the screen size out of `hdc shell hidumper -s RenderService -a screen`.
    ///
    /// A real RenderService dump reports several sizes on one line, e.g.
    /// <c>render resolution=1152x2520, physical resolution=1280x2800, ...</c> plus a separate
    /// <c>activeMode: 1280x2800, refreshRate=60</c>. The physical panel size is preferred because a
    /// wallpaper at native resolution is only ever downscaled for display, never upscaled.
    /// </summary>
    public static (int Width, int Height) ParseScreenSize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return (0, 0);
        }

        // First match wins for each label; the dump only reports each screen once in practice.
        var physical = MatchResolution(PhysicalResolutionPattern, output);
        if (physical.Width > 0)
        {
            return physical;
        }

        var render = MatchResolution(RenderResolutionPattern, output);
        if (render.Width > 0)
        {
            return render;
        }

        return MatchResolution(ActiveModePattern, output);
    }

    /// <summary>Matches the density scale factor reported by DisplayManagerService, e.g. <c>Density: 3.15</c>.</summary>
    private static readonly Regex DensityPattern = new(
        @"Density\s*[:=]\s*([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses the density out of `hdc shell hidumper -s DisplayManagerService -a -a` and converts it
    /// to Android's densityDpi units (scale x 160), so <see cref="Domain.Models.DisplayInfo.Density"/>
    /// means the same thing regardless of which transport discovered the device.
    /// </summary>
    /// <remarks>Returns 0 when the dump does not expose a density.</remarks>
    public static int ParseDensity(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return 0;
        }

        var match = DensityPattern.Match(output);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            return 0;
        }

        // A scale below 1 is not a density bucket; treat it as not-reported rather than guessing.
        if (scale < 1d)
        {
            return 0;
        }

        return (int)Math.Round(scale * 160d, MidpointRounding.AwayFromZero);
    }

    /// <summary>First match of <paramref name="pattern"/> rendered as a resolution, or (0,0).</summary>
    private static (int Width, int Height) MatchResolution(Regex pattern, string output)
    {
        var match = pattern.Match(output);
        if (!match.Success)
        {
            return (0, 0);
        }

        return (
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }
}
