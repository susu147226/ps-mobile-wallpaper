using Microsoft.Extensions.Logging;
using PSMobileWallpaper.Domain.Models;

namespace PSMobileWallpaper.Wallpaper.Providers;

/// <summary>
/// Base for the brand-specific Android providers in spec §18. Brand matching mirrors the
/// device-adapter rules so a device is never claimed by two different families.
/// </summary>
public abstract class BrandWallpaperProviderBase : AndroidHelperWallpaperProviderBase
{
    private readonly string[] _aliases;

    protected BrandWallpaperProviderBase(ILogger logger, string[] aliases, string helperApkPath)
        : base(logger, helperApkPath) => _aliases = aliases;

    public override bool CanHandle(DeviceInfo device)
    {
        if (device.Transport != Domain.Models.DeviceTransport.Adb)
        {
            return false;
        }

        return _aliases.Any(alias =>
            device.Brand.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
            device.Manufacturer.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }
}
