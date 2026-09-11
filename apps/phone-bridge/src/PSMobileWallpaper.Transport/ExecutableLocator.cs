namespace PSMobileWallpaper.Transport;

/// <summary>
/// Resolves the on-disk location of adb.exe / hdc.exe. An explicit path from config wins;
/// otherwise the tool is searched on PATH and in a few well-known install locations.
/// </summary>
public static class ExecutableLocator
{
    public static string Resolve(string executableName, string? configuredPath, params string[] additionalDirectories)
    {
        var fileName = OperatingSystem.IsWindows()
            ? $"{executableName}.exe"
            : executableName;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // Either a direct path to the executable, or a directory containing it.
            if (File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            if (Directory.Exists(configuredPath))
            {
                var candidate = Path.Combine(configuredPath, fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // Configured but missing: return it verbatim so callers surface *_NOT_FOUND
            // with the path the user actually configured.
            return configuredPath;
        }

        // Directories shipped alongside the bridge come before PATH, so a fresh install works
        // without the user installing or configuring anything.
        foreach (var directory in additionalDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // Malformed directory; skip it.
            }
        }

        var onPath = FindOnPath(fileName);
        if (onPath is not null)
        {
            return onPath;
        }

        return executableName;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        return null;
    }
}
