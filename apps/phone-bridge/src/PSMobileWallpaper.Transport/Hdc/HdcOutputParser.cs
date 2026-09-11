using System.Globalization;

namespace PSMobileWallpaper.Transport.Hdc;

/// <summary>One row of `hdc list targets`, before brand/model enrichment.</summary>
public sealed record HdcRawTarget(string Id, string State);

/// <summary>
/// Pure parsers for hdc output (spec §16), split out so they can be unit-tested without a device.
/// </summary>
public static class HdcOutputParser
{
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
    /// Parses `hdc shell hidumper -s RenderService -a screen` for the active screen size.
    /// HarmonyOS reports it as <c>activeMode: 1220x2700</c> inside the dump.
    /// </summary>
    public static (int Width, int Height) ParseScreenSize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return (0, 0);
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = line[..separator].Trim();
            if (!label.Equals("activeMode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();

            // Some builds append a refresh rate: "1220x2700 120".
            var space = value.IndexOf(' ');
            if (space > 0)
            {
                value = value[..space];
            }

            var parts = value.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return (width, height);
            }
        }

        return (0, 0);
    }
}
