using Android.App;
using Android.Content;
using Android.OS;
using Android.Wallpaper;
using Java.Interop;
using System.Text.Json;

namespace PSMobileWallpaper.Helper;

/// <summary>
/// Applies a wallpaper on behalf of the PhoneBridge.
///
/// Started by the bridge with <c>am start</c>; there is no UI and no launcher entry. The result is
/// written as JSON to a path the caller supplies, because `am start` cannot return a value.
///
/// Only the official <see cref="WallpaperManager"/> API is used. Nothing here roots the device,
/// modifies the system image, or bypasses a security check (spec §42).
/// </summary>
[Activity(
    Name = "com.psmobilewallpaper.helper.SetWallpaperActivity",
    Exported = true,
    Theme = "@android:style/Theme.NoDisplay",
    ExcludeFromRecents = true,
    NoHistory = true,
    Label = "PSMW Wallpaper Helper")]
public sealed class SetWallpaperActivity : Activity
{
    internal const string ExtraImagePath = "imagePath";
    internal const string ExtraTarget = "target";
    internal const string ExtraResultPath = "resultPath";

    /// <summary>WallpaperManager.FLAG_SYSTEM — the home screen.</summary>
    private const int FlagSystem = 1;

    /// <summary>WallpaperManager.FLAG_LOCK — the lock screen.</summary>
    private const int FlagLock = 2;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var imagePath = Intent?.GetStringExtra(ExtraImagePath);
        var target = Intent?.GetStringExtra(ExtraTarget) ?? "lock";
        var resultPath = Intent?.GetStringExtra(ExtraResultPath);

        var result = Apply(imagePath, target);

        if (!string.IsNullOrEmpty(resultPath))
        {
            try
            {
                File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                // Nothing else we can do; the bridge treats a missing result file as a failure.
                Android.Util.Log.Error("PSMW", $"Could not write result file: {ex.Message}");
            }
        }

        Finish();
    }

    private ApplyResult Apply(string? imagePath, string target)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return ApplyResult.Fail("IMAGE_NOT_FOUND", "No image path was supplied.");
        }

        if (!File.Exists(imagePath))
        {
            return ApplyResult.Fail("IMAGE_NOT_FOUND", $"Image not found on device: {imagePath}");
        }

        var manager = WallpaperManager.GetInstance(this);
        if (manager is null)
        {
            return ApplyResult.Fail("WALLPAPER_SET_FAILED", "WallpaperManager is unavailable on this device.");
        }

        var wantHome = target.Equals("home", StringComparison.OrdinalIgnoreCase) ||
                       target.Equals("both", StringComparison.OrdinalIgnoreCase);
        var wantLock = target.Equals("lock", StringComparison.OrdinalIgnoreCase) ||
                       target.Equals("both", StringComparison.OrdinalIgnoreCase);

        if (!wantHome && !wantLock)
        {
            return ApplyResult.Fail("WALLPAPER_SET_FAILED", $"Unknown wallpaper target '{target}'.");
        }

        var applied = new List<string>();
        var failures = new List<string>();

        if (wantHome)
        {
            try
            {
                ApplyTo(manager, imagePath, FlagSystem);
                applied.Add("home");
            }
            catch (Exception ex)
            {
                failures.Add($"home: {ex.Message}");
            }
        }

        if (wantLock)
        {
            try
            {
                ApplyTo(manager, imagePath, FlagLock);
                applied.Add("lock");
            }
            catch (Exception ex)
            {
                failures.Add($"lock: {ex.Message}");
            }
        }

        if (applied.Count == 0)
        {
            return ApplyResult.Fail("WALLPAPER_SET_FAILED", string.Join("; ", failures));
        }

        var message = $"Applied to: {string.Join(", ", applied)}.";
        if (failures.Count > 0)
        {
            message += $" Failed: {string.Join("; ", failures)}.";
        }

        return ApplyResult.Ok(message, applied);
    }

    /// <summary>
    /// WallpaperManager has no public overload that targets a specific screen, so the hidden
    /// four-argument <c>setStream(InputStream, Rect, boolean, int)</c> is invoked through JNI.
    /// The helper targets API 27 precisely so this remains permitted (see the csproj comment).
    /// </summary>
    private void ApplyTo(WallpaperManager manager, string imagePath, int which)
    {
        using var input = new Java.IO.FileInputStream(imagePath);

        var env = JNIEnv;
        var managerClass = env.GetObjectClass(manager.Handle);
        var methodId = env.GetMethodID(managerClass, "setStream", "(Ljava/io/InputStream;Landroid/graphics/Rect;ZI)V");

        if (methodId == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "WallpaperManager.setStream(InputStream, Rect, boolean, int) is not available on this Android build.");
        }

        var arguments = new JValue[]
        {
            new(input),
            new(IntPtr.Zero),   // visibleCropHint = null
            new(false),         // allowBackup = false
            new(which),
        };

        env.CallVoidMethod(manager.Handle, methodId, arguments);
    }

    private sealed record ApplyResult(bool Success, string Message, string? ErrorCode, IReadOnlyList<string> Applied)
    {
        public static ApplyResult Ok(string message, IReadOnlyList<string> applied) =>
            new(true, message, null, applied);

        public static ApplyResult Fail(string errorCode, string message) =>
            new(false, message, errorCode, []);
    }
}
