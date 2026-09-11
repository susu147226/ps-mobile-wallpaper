using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Infrastructure.Security;

namespace PSMobileWallpaper.Api.Security;

/// <summary>
/// Spec §23. Rejects any request that does not carry the per-install token. The token is accepted
/// from the <c>X-PSMW-Token</c> header, or from a <c>?token=</c> query parameter because browser
/// and UXP WebSocket clients cannot set request headers.
///
/// Also answers CORS preflights. That is not optional: the token travels in a custom header, which
/// makes every plugin request a "non-simple" CORS request that the runtime prefights with an
/// OPTIONS call. A preflight never carries the token by design, so requiring one there rejected the
/// preflight and the real request was never sent — the plugin saw PERMISSION_DENIED while curl
/// against the same endpoint succeeded.
/// </summary>
public sealed class LocalAuthMiddleware
{
    /// <summary>Health and liveness probes stay unauthenticated so the plugin can detect the bridge before it has the token.</summary>
    private static readonly string[] AnonymousPaths = ["/health"];

    private readonly RequestDelegate _next;

    public LocalAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILocalAuthTokenProvider tokens)
    {
        ApplyCorsHeaders(context);

        // A preflight performs no action — it only asks what is allowed — so letting it through
        // grants nothing. The real request that follows is still authenticated below.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

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

    /// <summary>
    /// Echoes the caller's origin back. The bridge is loopback-only and every action needs the token,
    /// so the origin is not what protects it — the token is. Without these headers the plugin cannot
    /// read any response at all.
    /// </summary>
    private static void ApplyCorsHeaders(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();

        context.Response.Headers.AccessControlAllowOrigin =
            string.IsNullOrEmpty(origin) ? "http://localhost" : origin;
        context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders =
            $"{LocalAuthTokenProvider.HttpHeaderName}, Content-Type, Accept";
        context.Response.Headers.Vary = "Origin";
    }
}
