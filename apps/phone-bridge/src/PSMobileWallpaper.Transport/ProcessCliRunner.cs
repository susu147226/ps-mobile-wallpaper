using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Transport.Abstractions;

namespace PSMobileWallpaper.Transport;

/// <summary>
/// Spawns the CLI tool directly with an argument vector — never through a shell — so device ids,
/// paths and shell payloads cannot be reinterpreted by cmd.exe.
/// </summary>
public sealed class ProcessCliRunner : ICliProcessRunner
{
    private readonly ILogger<ProcessCliRunner> _logger;

    public ProcessCliRunner(ILogger<ProcessCliRunner> logger) => _logger = logger;

    public async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new TransportException(
                ErrorCodes.TransportError,
                $"Executable not found at '{executablePath}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _logger.LogDebug("Running {Executable} with {ArgumentCount} argument(s).", executablePath, arguments.Count);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new TransportException(
                ErrorCodes.TransportError,
                $"Failed to start '{executablePath}': {ex.Message}",
                ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            var reason = cancellationToken.IsCancellationRequested
                ? "was cancelled"
                : $"timed out after {timeout.TotalSeconds:0}s";

            throw new TransportException(
                ErrorCodes.TransportError,
                $"'{Path.GetFileName(executablePath)}' {reason}.");
        }

        // Flush the async readers so no output is lost after exit.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to terminate the CLI process.");
        }
    }
}
