using Microsoft.Extensions.Options;
using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Infrastructure.Configuration;
using PSMobileWallpaper.Infrastructure.Security;

namespace PSMobileWallpaper.Api.Security;

/// <summary>
/// Spec §23 describes a local authentication token, gated here behind
/// <see cref="ServerOptions.RequireToken"/> (default off).
///
/// It is optional because UXP plugins cannot read %AppData%: the user would have to hand-copy the
/// token after every reinstall, and a stale copy looks exactly like a missing one. With the token
/// disabled the only barrier is the loopback-only binding below, so anything running as this user —
/// including a web page in this machine's browser — can drive the bridge.
///
/// Also answers CORS preflights. That is not optional: the token travels in a custom header, which
/// makes each request a "non-simple" CORS request that the runtime prefights with an OPTIONS call.
/// </summary>
public sealed class LocalAuthMiddleware
{
    /// <summary>Health and liveness probes stay unauthenticated so a client can detect the bridge before it has the token.</summary>
    private static readonly string[] AnonymousPaths = ["/health"];

    private readonly RequestDelegate _next;

    public LocalAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ILocalAuthTokenProvider tokens,
        IOptions<ServerOptions> options)
    {
        ApplyCorsHeaders(context);

        // A preflight performs no action — it only asks what is allowed — so letting it through
        // grants nothing. The real request that follows is still authenticated below.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!options.Value.RequireToken ||
            AnonymousPaths.Any(candidate => context.Request.Path.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
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
    /// Echoes the caller's origin back. The bridge is loopback-only, so the origin is not what
    /// protects it; without these headers the plugin cannot read any response at all.
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
