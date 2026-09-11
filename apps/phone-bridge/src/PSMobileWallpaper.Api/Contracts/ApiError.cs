namespace PSMobileWallpaper.Api.Contracts;

/// <summary>Spec §30. The uniform error body every failing endpoint returns.</summary>
public sealed record ApiError(bool Success, string ErrorCode, string Message)
{
    public static ApiError From(string errorCode, string message) => new(false, errorCode, message);
}
