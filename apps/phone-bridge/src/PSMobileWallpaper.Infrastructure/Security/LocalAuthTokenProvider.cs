using System.Security.Cryptography;

namespace PSMobileWallpaper.Infrastructure.Security;

/// <summary>
/// Spec §23. A per-install secret stored at %AppData%/PSMobileWallpaper/auth.token and required on
/// every request, so another local process cannot drive the bridge just by reaching loopback.
/// </summary>
public interface ILocalAuthTokenProvider
{
    string Token { get; }

    /// <summary>Constant-time comparison of a caller-supplied token.</summary>
    bool IsValid(string? candidate);
}

public sealed class LocalAuthTokenProvider : ILocalAuthTokenProvider
{
    private const string HeaderName = "X-PSMW-Token";

    public const string HttpHeaderName = HeaderName;

    private readonly string _token;

    public LocalAuthTokenProvider(string? tokenFilePath = null)
    {
        var path = tokenFilePath ?? Configuration.BridgePaths.TokenFilePath;

        Configuration.BridgePaths.EnsureCreated();
        _token = LoadOrCreate(path);
    }

    public string Token => _token;

    public bool IsValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length != _token.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(candidate),
            System.Text.Encoding.UTF8.GetBytes(_token));
    }

    private static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }

        var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        File.WriteAllText(path, created);

        return created;
    }
}
