using System.Globalization;

namespace PSMobileWallpaper.Transport.Adb;

/// <summary>One row of `adb devices -l`, before brand/model enrichment.</summary>
public sealed record AdbRawDevice(
    string Id,
    string RawState,
    string State);

/// <summary>
/// Pure parsers for adb output. Kept free of process concerns so they are unit-testable
/// without a physical device attached (spec §40: core algorithms must have unit tests).
/// </summary>
public static class AdbOutputParser
{
    /// <summary>
    /// Longest-prefix first: adb appends details after the state (<c>device product:...</c>) and
    /// reports multi-word states such as <c>no permissions (...)</c>.
    /// </summary>
    private static readonly (string Key, string Mapped)[] StateMap =
    [
        ("no permissions", "Unauthorized"),
        ("unauthorized", "Unauthorized"),
        ("offline", "Offline"),
        ("device", "Connected"),
        ("unknown", "Unknown"),
    ];

    /// <summary>Parses `adb devices -l`. The `List of devices attached` header and blank lines are ignored.</summary>
    public static IReadOnlyList<AdbRawDevice> ParseDevices(string? output)
    {
        var devices = new List<AdbRawDevice>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return devices;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // adb separates the serial from the state with a tab; the state itself may contain spaces.
            var tab = line.IndexOf('\t');
            string id;
            string rawState;

            if (tab > 0)
            {
                id = line[..tab].Trim();
                rawState = line[(tab + 1)..].Trim();
            }
            else
            {
                var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 2)
                {
                    continue;
                }

                id = columns[0];
                rawState = string.Join(' ', columns.Skip(1));
            }

            if (id.Length == 0 || rawState.Length == 0)
            {
                continue;
            }

            devices.Add(new AdbRawDevice(id, rawState, MapState(rawState)));
        }

        return devices;
    }

    private static string MapState(string rawState) =>
        StateMap.FirstOrDefault(
            candidate => rawState.StartsWith(candidate.Key, StringComparison.OrdinalIgnoreCase)).Mapped
        ?? "Unknown";

    /// <summary>
    /// Parses `adb shell getprop` output, e.g. <c>[ro.product.brand]: [Xiaomi]</c>,
    /// into a key/value map. Also tolerates bare `key: value` lines.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseGetProp(string? output)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return properties;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var key = string.Empty;
            var value = string.Empty;

            if (line.StartsWith('['))
            {
                var close = line.IndexOf(']');
                if (close <= 1)
                {
                    continue;
                }

                key = line[1..close];
                var rest = line[(close + 1)..].TrimStart();
                if (!rest.StartsWith(':'))
                {
                    continue;
                }

                value = rest[1..].Trim().TrimStart('[').TrimEnd(']');
            }
            else
            {
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                key = line[..colon].Trim();
                value = line[(colon + 1)..].Trim();
            }

            if (key.Length > 0)
            {
                properties[key] = value;
            }
        }

        return properties;
    }

    /// <summary>
    /// Parses `adb shell wm size`. Prefers an active override over the physical panel size,
    /// because the override is what the user actually sees.
    /// </summary>
    public static (int Width, int Height) ParseWmSize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return (0, 0);
        }

        (int Width, int Height) physical = (0, 0);
        (int Width, int Height) overridden = (0, 0);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = line[..separator].Trim();
            var size = ParseDimension(line[(separator + 1)..].Trim());

            if (label.Contains("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = size;
            }
            else if (label.Contains("Physical", StringComparison.OrdinalIgnoreCase))
            {
                physical = size;
            }
        }

        return overridden.Width > 0 ? overridden : physical;
    }

    /// <summary>Parses `adb shell wm density`, e.g. <c>Physical density: 480</c>. Prefers an override.</summary>
    public static int ParseWmDensity(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return 0;
        }

        var physical = 0;
        var overridden = 0;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = line[..separator].Trim();
            var value = ParseLeadingInt(line[(separator + 1)..]);

            if (label.Contains("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = value;
            }
            else if (label.Contains("Physical", StringComparison.OrdinalIgnoreCase))
            {
                physical = value;
            }
        }

        return overridden > 0 ? overridden : physical;
    }

    /// <summary>Parses `adb shell dumpsys input` for the surface rotation, e.g. <c>SurfaceOrientation: 0</c>.</summary>
    public static int ParseRotation(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return 0;
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
            if (label.Equals("SurfaceOrientation", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("rotation", StringComparison.OrdinalIgnoreCase))
            {
                return ParseLeadingInt(line[(separator + 1)..]);
            }
        }

        return 0;
    }

    private static (int Width, int Height) ParseDimension(string value)
    {
        var parts = value.Split('x', 'X');
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        var width = ParseLeadingInt(parts[0]);
        var height = ParseLeadingInt(parts[1]);
        return (width, height);
    }

    private static int ParseLeadingInt(string value)
    {
        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
