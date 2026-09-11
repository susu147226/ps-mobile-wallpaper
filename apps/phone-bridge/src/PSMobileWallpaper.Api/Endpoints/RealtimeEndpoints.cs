using System.Net.WebSockets;
using PSMobileWallpaper.Api.Realtime;

namespace PSMobileWallpaper.Api.Endpoints;

/// <summary>Spec §22. The <c>/ws</c> event stream.</summary>
public static class RealtimeEndpoints
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    public static void MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/ws", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext context,
        EventBroadcaster broadcaster,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("WebSocket");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var clientId = broadcaster.Add(socket);

        try
        {
            await PumpAsync(socket, logger, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client went away or the server is shutting down.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "WebSocket client {ClientId} dropped.", clientId);
        }
        finally
        {
            broadcaster.Remove(clientId);
        }
    }

    /// <summary>
    /// Reads (and discards) client frames. The plugin is not expected to send anything, but the read
    /// loop is what keeps the socket open and surfaces a clean close.
    /// </summary>
    private static async Task PumpAsync(WebSocket socket, ILogger logger, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        while (socket.State == WebSocketState.Open)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(IdleTimeout);

            WebSocketReceiveResult result;
            try
            {
                result = await socket
                    .ReceiveAsync(buffer, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Closing idle WebSocket client.");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken)
                    .ConfigureAwait(false);

                break;
            }
        }
    }
}
