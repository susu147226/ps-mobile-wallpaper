using System.Text.Json.Serialization;

namespace PSMobileWallpaper.Api.Realtime;

/// <summary>Spec §22. The complete event vocabulary published on <c>/ws</c>.</summary>
public static class BridgeEventNames
{
    public const string DeviceConnected = "device.connected";
    public const string DeviceDisconnected = "device.disconnected";
    public const string DeviceUpdated = "device.updated";
    public const string TransferStarted = "transfer.started";
    public const string TransferProgress = "transfer.progress";
    public const string TransferCompleted = "transfer.completed";
    public const string TransferFailed = "transfer.failed";
    public const string WallpaperStarted = "wallpaper.started";
    public const string WallpaperCompleted = "wallpaper.completed";
    public const string WallpaperFailed = "wallpaper.failed";
}

/// <summary>Spec §22 envelope: <c>{ "event": "...", "data": { ... } }</c>.</summary>
public sealed class BridgeEvent
{
    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("data")]
    public required object Data { get; init; }
}

/// <summary>Payload for the device.* events (spec §22 example).</summary>
public sealed record DeviceEventPayload(string DeviceId, string Brand, string Model);

/// <summary>Payload for the transfer.* events.</summary>
public sealed record TransferEventPayload(
    string DeviceId,
    string Direction,
    string? LocalPath,
    string? RemotePath,
    long BytesTransferred = 0,
    long TotalBytes = 0)
{
    /// <summary>0-100. Zero when the total size is unknown.</summary>
    public int Percent => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesTransferred * 100 / TotalBytes, 0, 100);
}

/// <summary>Payload for the wallpaper.* events.</summary>
public sealed record WallpaperEventPayload(
    string DeviceId,
    string Target,
    bool? Success = null,
    string? ErrorCode = null,
    string? Message = null);
