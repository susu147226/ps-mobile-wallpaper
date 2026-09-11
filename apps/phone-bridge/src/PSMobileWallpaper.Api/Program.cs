using Microsoft.Extensions.Options;
using PSMobileWallpaper.Api.Endpoints;
using PSMobileWallpaper.Api.Realtime;
using PSMobileWallpaper.Api.Security;
using PSMobileWallpaper.Application;
using PSMobileWallpaper.Device;
using PSMobileWallpaper.Device.Abstractions;
using PSMobileWallpaper.Device.Adapters;
using PSMobileWallpaper.Domain.Models;
using PSMobileWallpaper.Image;
using PSMobileWallpaper.Image.Abstractions;
using PSMobileWallpaper.Infrastructure.Configuration;
using PSMobileWallpaper.Infrastructure.Logging;
using PSMobileWallpaper.Infrastructure.Security;
using PSMobileWallpaper.Transport;
using PSMobileWallpaper.Transport.Abstractions;
using PSMobileWallpaper.Transport.Adb;
using PSMobileWallpaper.Transport.Hdc;
using PSMobileWallpaper.Wallpaper;
using PSMobileWallpaper.Wallpaper.Abstractions;
using PSMobileWallpaper.Wallpaper.Providers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (spec §2.6) --------------------------------------------------
Log.Logger = LoggingSetup.Create().CreateLogger();
builder.Host.UseSerilog();

// ---- Configuration (spec §2.7 / §24) --------------------------------------
builder.Configuration.AddConfiguration(BridgeConfiguration.Load());

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.Configure<AdbOptions>(builder.Configuration.GetSection(AdbOptions.SectionName));
builder.Services.Configure<HdcOptions>(builder.Configuration.GetSection(HdcOptions.SectionName));
builder.Services.Configure<ImageOptions>(builder.Configuration.GetSection(ImageOptions.SectionName));
builder.Services.Configure<WallpaperOptions>(builder.Configuration.GetSection(WallpaperOptions.SectionName));

// ---- Cross-cutting --------------------------------------------------------
builder.Services.AddSingleton<ICliProcessRunner, ProcessCliRunner>();
builder.Services.AddSingleton<ILocalAuthTokenProvider, LocalAuthTokenProvider>();
builder.Services.AddSingleton<EventBroadcaster>();

// ---- Transports (spec §14 / §15 / §16) ------------------------------------
builder.Services.AddSingleton<IDeviceTransport>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AdbOptions>>().Value;

    return new AdbTransport(
        sp.GetRequiredService<ICliProcessRunner>(),
        sp.GetRequiredService<ILogger<AdbTransport>>(),
        options.Path);
});

builder.Services.AddSingleton<IDeviceTransport>(sp =>
{
    var options = sp.GetRequiredService<IOptions<HdcOptions>>().Value;

    return new HdcTransport(
        sp.GetRequiredService<ICliProcessRunner>(),
        sp.GetRequiredService<ILogger<HdcTransport>>(),
        options.Path);
});

// ---- Device discovery (spec §5 / §25 / §26) -------------------------------
builder.Services.AddSingleton<DeviceProber>();
builder.Services.AddSingleton<DisplayProbe>();

// Order matters: brand adapters claim their devices before the generic Android fallback.
builder.Services.AddSingleton<IDeviceAdapter, HuaweiDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, HonorDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, XiaomiDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, OppoDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, VivoDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, HarmonyDeviceAdapter>();
builder.Services.AddSingleton<IDeviceAdapter, AndroidDeviceAdapter>();

builder.Services.AddSingleton<IDeviceManager>(sp => new DeviceManager(
    sp.GetRequiredService<IEnumerable<IDeviceTransport>>(),
    sp.GetRequiredService<IEnumerable<IDeviceAdapter>>(),
    sp.GetRequiredService<DeviceProber>(),
    sp.GetRequiredService<DisplayProbe>(),
    sp.GetRequiredService<ILogger<DeviceManager>>()));

// ---- Image processing (spec §2.5 / §12) -----------------------------------
builder.Services.AddSingleton<IImageProcessor>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ImageOptions>>().Value;

    return new SkiaImageProcessor(
        sp.GetRequiredService<ILogger<SkiaImageProcessor>>(),
        BridgePaths.TempWorkspaceDirectory,
        ParseFormat(options.Format),
        options.Quality);
});

// ---- Wallpaper (spec §17 / §18) -------------------------------------------
// Android providers receive the bundled helper APK; they install it on demand and only advertise
// lock/home support once it is actually present on the device (spec §40).
var helperApkPath = BridgePaths.HelperApkPath;

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new HuaweiWallpaperProvider(sp.GetRequiredService<ILogger<HuaweiWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new HonorWallpaperProvider(sp.GetRequiredService<ILogger<HonorWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new XiaomiWallpaperProvider(sp.GetRequiredService<ILogger<XiaomiWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new OppoWallpaperProvider(sp.GetRequiredService<ILogger<OppoWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new VivoWallpaperProvider(sp.GetRequiredService<ILogger<VivoWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperProvider, HarmonyWallpaperProvider>();

builder.Services.AddSingleton<IWallpaperProvider>(sp =>
    new AndroidWallpaperProvider(sp.GetRequiredService<ILogger<AndroidWallpaperProvider>>(), helperApkPath));

builder.Services.AddSingleton<IWallpaperService>(sp => new WallpaperService(
    sp.GetRequiredService<IEnumerable<IWallpaperProvider>>(),
    sp.GetRequiredService<IEnumerable<IDeviceTransport>>(),
    sp.GetRequiredService<ILogger<WallpaperService>>(),
    sp.GetRequiredService<IOptions<WallpaperOptions>>().Value.SaveToGallery));

// ---- Application ----------------------------------------------------------
builder.Services.AddSingleton<WallpaperWorkflow>();

// ---- Background workers ---------------------------------------------------
builder.Services.AddHostedService<DeviceEventForwarder>();

var app = builder.Build();

// ---- Loopback-only binding (spec §23) -------------------------------------
var serverOptions = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

if (!serverOptions.IsLoopbackOnly)
{
    throw new InvalidOperationException(
        $"Spec §23 requires loopback-only binding, but server.host is '{serverOptions.Host}'. Use 127.0.0.1.");
}

app.Urls.Clear();
app.Urls.Add($"http://{serverOptions.Host}:{serverOptions.Port}");

// ---- Pipeline -------------------------------------------------------------
app.UseWebSockets();
app.UseMiddleware<LocalAuthMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.0.0" }));
app.MapDeviceEndpoints();
app.MapImageEndpoints();
app.MapWallpaperEndpoints();
app.MapRealtimeEndpoints();

Log.Information(
    "PS Mobile Wallpaper PhoneBridge listening on http://{Host}:{Port} (API prefix {ApiPrefix}).",
    serverOptions.Host, serverOptions.Port, "/api/v1");

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PhoneBridge terminated unexpectedly.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static ImageFormat ParseFormat(string? value) =>
    value?.Equals("jpg", StringComparison.OrdinalIgnoreCase) == true ||
    value?.Equals("jpeg", StringComparison.OrdinalIgnoreCase) == true
        ? ImageFormat.Jpeg
        : ImageFormat.Png;
