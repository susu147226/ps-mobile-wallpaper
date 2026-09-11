using Serilog;
using Serilog.Events;

namespace PSMobileWallpaper.Infrastructure.Logging;

/// <summary>Spec §2.6. Serilog writing daily-rolling files into %AppData%/PSMobileWallpaper/logs/.</summary>
public static class LoggingSetup
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static LoggerConfiguration Create(string? logDirectory = null)
    {
        var directory = logDirectory ?? Configuration.BridgePaths.LogDirectory;
        Directory.CreateDirectory(directory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.File(
                Path.Combine(directory, "psmw-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: OutputTemplate);
    }
}
