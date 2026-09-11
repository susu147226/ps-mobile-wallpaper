namespace PSMobileWallpaper.Domain.Models;

using System.Text.Json.Serialization;

/// <summary>Spec §5.4 / §28. Unified across ADB and HDC.</summary>
public sealed class DeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public DeviceTransport Transport { get; set; }
    public DeviceState State { get; set; } = DeviceState.Unknown;
    public DisplayInfo? Display { get; set; }

    /// <summary>User-facing label, e.g. "HUAWEI XXX". Spec §9 — the UI composes this itself.</summary>
    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Brand) ? Model : $"{Brand} {Model}".Trim();
}
