using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Infrastructure.Security;

namespace PSMobileWallpaper.Api.Security;

/// <summary>
/// Spec §23. Rejects any request that does not carry the per-install token. The token is accepted
/// from the <c>X-PSMW-Token</c> header, or from a <c>?token=</c> query parameter because browser
/// and UXP WebSocket clients cannot set request headers.
/// </summary>
public sealed class LocalAuthMiddleware
{
    /// <summary>Health and liveness probes stay unauthenticated so the plugin can detect the bridge before it has the token.</summary>
    private static readonly string[] AnonymousPaths = ["/health"];

    private readonly RequestDelegate _next;

    public LocalAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILocalAuthTokenProvider tokens)
    {
        var path = context.Request.Path;

        if (AnonymousPaths.Any(candidate => path.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var candidate = context.Request.Headers[LocalAuthTokenProvider.HttpHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(candidate))
        {
            candidate = context.Request.Query["token"].FirstOrDefault();
        }

        if (!tokens.IsValid(candidate))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response
                .WriteAsJsonAsync(ApiError.From(ErrorCodes.PermissionDenied, "A valid local authentication token is required."))
                .ConfigureAwait(false);

            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
