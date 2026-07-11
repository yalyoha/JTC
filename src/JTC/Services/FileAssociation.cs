using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JTC.Services;

/// <summary>
/// Registers JTC as a handler for <c>.torrent</c> files in the current user's
/// registry hive. No admin required. Idempotent: safe to call on every startup —
/// only rewrites keys when a value changed.
/// </summary>
public static class FileAssociation
{
    private const string ProgId       = "JTC.Torrent";
    private const string OldProgId    = "TClient.Torrent"; // pre-rebrand — remove on next launch
    private const string FriendlyName = "Файл торрента (JTC)";
    private const string Extension    = ".torrent";

    // SHChangeNotify constants
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST       = 0x0000;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "JTC.exe");
            RemoveLegacyProgId();
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tclient.ico");
            // Fall back to embedded exe icon if the file asset is missing for any reason.
            var iconRef = File.Exists(iconPath) ? $"\"{iconPath}\",0" : $"\"{exePath}\",0";
            var openCommand = $"\"{exePath}\" \"%1\"";

            var changed = false;

            // 1. ProgID: HKCU\Software\Classes\JTC.Torrent
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}", writable: true))
            {
                changed |= SetValueIfDifferent(progIdKey, null, FriendlyName);
                using var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true);
                changed |= SetValueIfDifferent(iconKey, null, iconRef);
                using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command", writable: true);
                changed |= SetValueIfDifferent(cmdKey, null, openCommand);
            }

            // 2. Extension: HKCU\Software\Classes\.torrent — set OpenWithProgIds entry.
            //    We do NOT overwrite the (default) value to avoid stealing the default handler
            //    from another torrent client that's already installed system-wide. Explorer
            //    still lists us in "Open with" thanks to OpenWithProgIds.
            using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Extension}", writable: true))
            using (var progIdsKey = extKey.CreateSubKey("OpenWithProgIds", writable: true))
            {
                if (progIdsKey.GetValue(ProgId) is null)
                {
                    progIdsKey.SetValue(ProgId, string.Empty, RegistryValueKind.String);
                    changed = true;
                }
            }

            // 3. Applications entry — makes "Open with" show JTC with the right name/icon
            //    even when the file has no default association.
            using (var appsKey = Registry.CurrentUser.CreateSubKey(
                       $@"Software\Classes\Applications\{Path.GetFileName(exePath)}", writable: true))
            {
                changed |= SetValueIfDifferent(appsKey, "FriendlyAppName", "Junior Torrent Client");
                using var iconKey = appsKey.CreateSubKey("DefaultIcon", writable: true);
                changed |= SetValueIfDifferent(iconKey, null, iconRef);
                using var cmdKey = appsKey.CreateSubKey(@"shell\open\command", writable: true);
                changed |= SetValueIfDifferent(cmdKey, null, openCommand);
                using var typesKey = appsKey.CreateSubKey("SupportedTypes", writable: true);
                if (typesKey.GetValue(Extension) is null)
                {
                    typesKey.SetValue(Extension, string.Empty, RegistryValueKind.String);
                    changed = true;
                }
            }

            if (changed)
            {
                // 1) Tell shell subscribers that file associations changed.
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                // 2) SHChangeNotify alone doesn't invalidate Explorer's icon-cache DB, so
                //    already-visible .torrent files keep their stale thumbnail. `ie4uinit -show`
                //    is the classic shell-refresh hook that forces Explorer to re-query icons.
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ie4uinit.exe",
                        Arguments = "-show",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    })?.Dispose();
                }
                catch (Exception ex) { DebugLog.Error("ie4uinit -show", ex); }
                DebugLog.Info("FileAssociation: registry updated, shell refresh triggered");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("FileAssociation.EnsureRegistered", ex);
            // Non-fatal — the app still works, just without file associations.
        }
    }

    private static bool SetValueIfDifferent(RegistryKey key, string? name, string desired)
    {
        var existing = key.GetValue(name) as string;
        if (string.Equals(existing, desired, StringComparison.Ordinal))
            return false;
        key.SetValue(name, desired, RegistryValueKind.String);
        return true;
    }

    /// <summary>
    /// Cleans up the pre-rebrand HKCU registry entries that pointed at TClient.Torrent /
    /// TClient.exe. Safe if they're absent. Runs on every startup — idempotent.
    /// </summary>
    private static void RemoveLegacyProgId()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{OldProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\TClient.exe", throwOnMissingSubKey: false);
            using var owp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Extension}\OpenWithProgIds", writable: true);
            owp?.DeleteValue(OldProgId, throwOnMissingValue: false);
        }
        catch (Exception ex) { DebugLog.Error("RemoveLegacyProgId", ex); }
    }
}
