using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PSMobileWallpaper.Api.Realtime;

/// <summary>
/// Spec §22. Fan-out for bridge events. Slow or dead clients are dropped rather than allowed to
/// block the publisher, so a stalled plugin cannot hold up device polling.
/// </summary>
public sealed class EventBroadcaster
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly ILogger<EventBroadcaster> _logger;

    public EventBroadcaster(ILogger<EventBroadcaster> logger) => _logger = logger;

    public int ClientCount => _clients.Count;

    public Guid Add(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        _logger.LogInformation("WebSocket client {ClientId} connected ({Count} total).", id, _clients.Count);

        return id;
    }

    public void Remove(Guid id)
    {
        if (_clients.TryRemove(id, out _))
        {
            _logger.LogInformation("WebSocket client {ClientId} disconnected ({Count} remaining).", id, _clients.Count);
        }
    }

    public async Task PublishAsync(BridgeEvent bridgeEvent, CancellationToken cancellationToken = default)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(bridgeEvent, SerializerOptions);
        var message = Encoding.UTF8.GetString(payload);

        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open)
            {
                Remove(id);
                continue;
            }

            try
            {
                await socket
                    .SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Dropping WebSocket client {ClientId}: {Message}", id, message);
                Remove(id);
            }
        }
    }

    public static BridgeEvent Create(string eventName, object data) => new()
    {
        Event = eventName,
        Data = data,
    };
}
