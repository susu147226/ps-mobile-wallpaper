using System.Text.Json;
using PSMobileWallpaper.Api.Contracts;
using PSMobileWallpaper.Api.Realtime;
using PSMobileWallpaper.Domain.Errors;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Tests.Contracts;

/// <summary>
/// Spec §34 Phase 5. Locks the wire format the UXP plugin is written against. These assertions are
/// intentionally strict about the *exact* property set: an extra computed property leaking into the
/// payload would otherwise go unnoticed until a plugin build broke.
/// </summary>
public sealed class WireContractTests
{
    /// <summary>
    /// Mirrors ASP.NET Core's defaults for minimal APIs (<see cref="JsonSerializerDefaults.Web"/>):
    /// camelCase, and null values are *kept* — verified against a live bridge response.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialize<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Options)).RootElement;

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();

    [Fact]
    public void DeviceInfo_MatchesSpecSection5_4()
    {
        var json = Serialize(new DeviceInfo
        {
            Id = "ABC123",
            Brand = "HUAWEI",
            Manufacturer = "HUAWEI",
            Model = "XXX",
            Os = "Android",
            OsVersion = "12",
            Transport = DeviceTransport.Adb,
            State = DeviceState.Connected,
        });

        Assert.Equal(
            new[] { "brand", "display", "id", "manufacturer", "model", "os", "osVersion", "state", "transport" },
            PropertyNames(json));

        Assert.Equal("ADB", json.GetProperty("transport").GetString());
    }

    [Fact]
    public void DeviceTransport_SerializesUppercase_ForBothKinds()
    {
        Assert.Equal("ADB", Serialize(DeviceTransport.Adb).GetString());
        Assert.Equal("HDC", Serialize(DeviceTransport.Hdc).GetString());
    }

    [Theory]
    [InlineData(DeviceState.Connected, "Connected")]
    [InlineData(DeviceState.Unauthorized, "Unauthorized")]
    [InlineData(DeviceState.Offline, "Offline")]
    [InlineData(DeviceState.Unknown, "Unknown")]
    public void DeviceState_SerializesAsAReadableString(DeviceState state, string expected)
    {
        Assert.Equal(expected, Serialize(state).GetString());
    }

    [Fact]
    public void DisplayInfo_MatchesSpecSection6_AndOmitsComputedMembers()
    {
        var json = Serialize(new DisplayInfo
        {
            Width = 1220,
            Height = 2700,
            Density = 540,
            Rotation = 0,
            Orientation = ScreenOrientation.Portrait,
        });

        Assert.Equal(
            new[] { "density", "height", "orientation", "rotation", "width" },
            PropertyNames(json));

        Assert.Equal("Portrait", json.GetProperty("orientation").GetString());
    }

    [Fact]
    public void WallpaperCapabilities_MatchesSpecSection19()
    {
        var json = Serialize(new WallpaperCapabilities());

        Assert.Equal(
            new[] { "canSaveToGallery", "canSetBoth", "canSetHome", "canSetLock", "requiresUserConfirmation" },
            PropertyNames(json));
    }

    [Fact]
    public void WallpaperResult_MatchesSpecSection29()
    {
        var json = Serialize(WallpaperResult.Fail("ABC123", ErrorCodes.WallpaperNotSupported, "nope"));

        Assert.Equal(
            new[] { "deviceId", "errorCode", "message", "success" },
            PropertyNames(json));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("WALLPAPER_NOT_SUPPORTED", json.GetProperty("errorCode").GetString());
    }

    [Fact]
    public void ApiError_MatchesSpecSection30()
    {
        var json = Serialize(ApiError.From(ErrorCodes.DeviceNotFound, "missing"));

        Assert.Equal(new[] { "errorCode", "message", "success" }, PropertyNames(json));
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void BridgeEvent_UsesTheSection22Envelope()
    {
        var json = Serialize(EventBroadcaster.Create(
            BridgeEventNames.DeviceConnected,
            new DeviceEventPayload("ABC123", "XIAOMI", "XXX")));

        Assert.Equal(new[] { "data", "event" }, PropertyNames(json));
        Assert.Equal("device.connected", json.GetProperty("event").GetString());

        var data = json.GetProperty("data");
        Assert.Equal(new[] { "brand", "deviceId", "model" }, PropertyNames(data));
        Assert.Equal("ABC123", data.GetProperty("deviceId").GetString());
    }

    [Fact]
    public void TransferEventPayload_ExposesProgressPercent()
    {
        var json = Serialize(new TransferEventPayload("ABC123", "push", "/a.png", "/b.png", 850_000, 1_200_000));

        Assert.Equal(70, json.GetProperty("percent").GetInt32());
    }

    [Fact]
    public void TransferEventPayload_PercentIsZeroWhenTotalUnknown()
    {
        var json = Serialize(new TransferEventPayload("ABC123", "push", null, null, 512, 0));

        Assert.Equal(0, json.GetProperty("percent").GetInt32());
    }

    [Fact]
    public void ErrorCodes_AreExactlyTheSetDefinedBySpecSection30()
    {
        var declared = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(value => value)
            .ToArray();

        var expected = new[]
        {
            "ADB_NOT_FOUND", "DEVICE_NOT_FOUND", "DEVICE_OFFLINE", "DEVICE_UNAUTHORIZED",
            "DISPLAY_INFO_FAILED", "HDC_NOT_FOUND", "IMAGE_NOT_FOUND", "IMAGE_PROCESS_FAILED",
            "IMAGE_UPLOAD_FAILED", "PERMISSION_DENIED", "TRANSPORT_ERROR", "UNKNOWN_ERROR",
            "WALLPAPER_NOT_SUPPORTED", "WALLPAPER_SET_FAILED",
        };

        Assert.Equal(expected, declared);
    }

    [Fact]
    public void EventNames_AreExactlyTheSetDefinedBySpecSection22()
    {
        var declared = typeof(BridgeEventNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(value => value)
            .ToArray();

        var expected = new[]
        {
            "device.connected", "device.disconnected", "device.updated",
            "transfer.completed", "transfer.failed", "transfer.progress", "transfer.started",
            "wallpaper.completed", "wallpaper.failed", "wallpaper.started",
        };

        Assert.Equal(expected, declared);
    }
}
