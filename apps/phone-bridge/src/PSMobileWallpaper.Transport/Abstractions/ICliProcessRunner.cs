namespace PSMobileWallpaper.Transport.Abstractions;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError)
            ? StandardOutput
            : $"{StandardOutput}{Environment.NewLine}{StandardError}";
}

/// <summary>
/// Runs an external CLI tool. Kept as an interface so transports stay testable without adb/hdc on disk.
/// </summary>
public interface ICliProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
