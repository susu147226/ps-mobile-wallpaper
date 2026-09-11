using Android.App;
using Android.Content;
using Android.OS;
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

    /// <summary>
    /// Result file name inside the app's private files directory.
    /// The bridge reads it with <c>run-as</c>: since Android 11 the shell cannot see
    /// <c>/sdcard/Android/data/...</c>, and the app cannot write to <c>/data/local/tmp</c>.
    /// </summary>
    internal const string ResultFileName = "psmw-result.json";

    /// <summary>WallpaperManager.FLAG_SYSTEM — the home screen.</summary>
    private const int FlagSystem = 1;

    /// <summary>WallpaperManager.FLAG_LOCK — the lock screen.</summary>
    private const int FlagLock = 2;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var imagePath = Intent?.GetStringExtra(ExtraImagePath);
        var target = Intent?.GetStringExtra(ExtraTarget) ?? "lock";

        var result = Apply(imagePath, target);
        WriteResult(result);

        Finish();
    }

    private void WriteResult(ApplyResult result)
    {
        try
        {
            var path = Path.Combine(FilesDir!.AbsolutePath, ResultFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(result));
            Android.Util.Log.Info("PSMW", $"Result written to {path}");
        }
        catch (Exception ex)
        {
            // Nothing else we can do; the bridge treats a missing result file as a failure.
            Android.Util.Log.Error("PSMW", $"Could not write result file: {ex.Message}");
        }
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
    private unsafe void ApplyTo(WallpaperManager manager, string imagePath, int which)
    {
        using var input = new Java.IO.FileInputStream(imagePath);

        var instance = new JniObjectReference(manager.Handle);
        var classRef = JniEnvironment.Types.GetObjectClass(instance);

        // GetMethodID throws when the member is absent, so the diagnostic has to be in the catch.
        //
        // Note the trailing "I": this overload returns int (the new wallpaper id), not void. It is
        // easily mistaken for void — the reflection dump on a real device is what settled it.
        JniMethodInfo method;
        try
        {
            method = JniEnvironment.InstanceMethods.GetMethodID(
                classRef,
                "setStream",
                "(Ljava/io/InputStream;Landroid/graphics/Rect;ZI)I");
        }
        catch (Exception ex)
        {
            // Do not guess at the signature: report what this Android build actually exposes.
            throw new InvalidOperationException(
                "WallpaperManager has no setStream(InputStream, Rect, boolean, int). Available: " +
                DescribeWallpaperMethods() + $" [{ex.GetType().Name}]");
        }

        // setStream(InputStream, Rect, boolean, int)
        var arguments = stackalloc JniArgumentValue[4];
        arguments[0] = new JniArgumentValue(input.Handle);
        arguments[1] = new JniArgumentValue(IntPtr.Zero);   // visibleCropHint = null
        arguments[2] = new JniArgumentValue(false);         // allowBackup = false
        arguments[3] = new JniArgumentValue(which);         // FLAG_SYSTEM / FLAG_LOCK

        JniEnvironment.InstanceMethods.CallIntMethod(instance, method, arguments);
    }

    /// <summary>Lists every declared setStream/setBitmap overload, for diagnosing signature drift.</summary>
    private static string DescribeWallpaperMethods()
    {
        try
        {
            var klass = Java.Lang.Class.FromType(typeof(WallpaperManager));
            var described = new List<string>();

            foreach (var candidate in klass.GetDeclaredMethods() ?? [])
            {
                var name = candidate.Name ?? string.Empty;
                if (!name.Contains("etStream", StringComparison.Ordinal) &&
                    !name.Contains("etBitmap", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = candidate.GetParameterTypes() ?? [];
                var parameterNames = parameters.Select(type => type?.Name ?? "?");

                described.Add($"{name}({string.Join(",", parameterNames)})->{candidate.ReturnType?.Name}");
            }

            return described.Count == 0 ? "<none found>" : string.Join(" | ", described);
        }
        catch (Exception ex)
        {
            return $"<reflection failed: {ex.Message}>";
        }
    }

    private sealed record ApplyResult(bool Success, string Message, string? ErrorCode, IReadOnlyList<string> Applied)
    {
        public static ApplyResult Ok(string message, IReadOnlyList<string> applied) =>
            new(true, message, null, applied);

        public static ApplyResult Fail(string errorCode, string message) =>
            new(false, message, errorCode, []);
    }
}
