using PSMobileWallpaper.Domain.Errors;

namespace PSMobileWallpaper.Transport.Abstractions;

/// <summary>
/// Raised by the transport layer for any device-communication failure. Always carries one of
/// the unified <see cref="ErrorCodes"/> (spec §30) so upper layers never invent their own.
/// </summary>
public sealed class TransportException : Exception
{
    public string ErrorCode { get; }

    public TransportException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public static TransportException ExecutableNotFound(string executableName, string path) =>
        new(
            executableName.Equals("adb", StringComparison.OrdinalIgnoreCase)
                ? ErrorCodes.AdbNotFound
                : ErrorCodes.HdcNotFound,
            $"'{executableName}' was not found or is not executable at '{path}'.");
}
