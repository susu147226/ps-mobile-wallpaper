using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Transport.Adb;
using PSMobileWallpaper.Transport.Hdc;

namespace PSMobileWallpaper.Device;

/// <summary>
/// Reads the raw identity properties from a device over whichever transport it uses (spec §5.4).
/// Failures degrade to an empty probe rather than throwing, so one unresponsive handset
/// cannot break device enumeration for the others.
/// </summary>
public sealed class DeviceProber
{
    private static readonly string[] HarmonyKeys =
    [
        "const.product.brand",
        "const.product.manufacturer",
        "const.product.model",
        "const.ohos.fullname",
        "const.product.software.version",
    ];

    private readonly ILogger<DeviceProber> _logger;

    public DeviceProber(ILogger<DeviceProber> logger) => _logger = logger;

    public async Task<Abstractions.DeviceProbe> ProbeAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return transport.Kind switch
            {
                DeviceTransport.Adb => await ProbeAndroidAsync(device, transport, cancellationToken).ConfigureAwait(false),
                DeviceTransport.Hdc => await ProbeHarmonyAsync(device, transport, cancellationToken).ConfigureAwait(false),
                _ => Empty(device),
            };
        }
        catch (Exception ex) when (ex is TransportException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to probe device {DeviceId}; falling back to identity-only info.", device.Id);
            return Empty(device);
        }
    }

    private static async Task<Abstractions.DeviceProbe> ProbeAndroidAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var output = await transport.ShellAsync(device.Id, "getprop", cancellationToken).ConfigureAwait(false);

        return new Abstractions.DeviceProbe
        {
            Device = device,
            Properties = AdbOutputParser.ParseGetProp(output),
        };
    }

    private static async Task<Abstractions.DeviceProbe> ProbeHarmonyAsync(
        DeviceInfo device,
        IDeviceTransport transport,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in HarmonyKeys)
        {
            var value = await transport
                .ShellAsync(device.Id, $"param get {key}", cancellationToken)
                .ConfigureAwait(false);

            var parsed = HdcOutputParser.ParseParamGet(value);
            if (parsed.Length > 0)
            {
                properties[key] = parsed;
            }
        }

        var osVersion = NormalizeHarmonyVersion(properties.GetValueOrDefault("const.ohos.fullname", string.Empty));

        return new Abstractions.DeviceProbe
        {
            Device = device,
            Properties = properties,
            OsName = "HarmonyOS",
            OsVersion = osVersion,
        };
    }

    /// <summary>
    /// `const.ohos.fullname` reads like "OpenHarmony-7.0.0.105"; users recognise the trailing version.
    /// `const.ohos.apiversion` is deliberately not used — it is an API level (e.g. "26"), not an OS version.
    /// </summary>
    private static string NormalizeHarmonyVersion(string fullName)
    {
        var trimmed = fullName.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var dash = trimmed.LastIndexOf('-');

        return dash >= 0 && dash < trimmed.Length - 1 ? trimmed[(dash + 1)..] : trimmed;
    }

    private static Abstractions.DeviceProbe Empty(DeviceInfo device) => new()
    {
        Device = device,
        Properties = new Dictionary<string, string>(),
    };
}
