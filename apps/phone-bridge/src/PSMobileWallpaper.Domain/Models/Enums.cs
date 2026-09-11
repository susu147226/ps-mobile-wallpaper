namespace PSMobileWallpaper.Domain.Models;

using System.Text.Json.Serialization;
using PSMobileWallpaper.Domain.Serialization;

/// <summary>
/// Unified device state. Covers both the transport-level states reported by
/// <c>adb devices</c> / <c>hdc list targets</c> (spec §5.2) and the UI states in spec §32.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceState>))]
public enum DeviceState
{
    Unknown = 0,
    Disconnected = 1,
    Connecting = 2,
    Connected = 3,
    Unauthorized = 4,
    Offline = 5,
    Error = 6,
}

/// <summary>Transport that discovered the device. Serialized as "ADB" / "HDC" per spec §5.4.</summary>
[JsonConverter(typeof(UppercaseEnumConverter<DeviceTransport>))]
public enum DeviceTransport
{
    Adb = 0,
    Hdc = 1,
}

/// <summary>Screen orientation. Serialized as "Portrait" / "Landscape".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreenOrientation>))]
public enum ScreenOrientation
{
    Portrait = 0,
    Landscape = 1,
}
