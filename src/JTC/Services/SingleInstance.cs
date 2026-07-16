using System.Diagnostics;

namespace JTC.Services;

/// <summary>
/// Cross-process single-instance guard for the current user session, with a file-based
/// "inbox" that lets subsequent launches hand off their .torrent arguments to the primary
/// instance and exit cleanly.
///
/// Usage in <c>App.OnLaunched</c>:
/// <code>
///   if (SingleInstance.TryClaimOrHandOff(args)) return; // we're a secondary, already handed off
///   // else we're primary — proceed with UI setup, later call StartWatching()
/// </code>
/// </summary>
public static class SingleInstance
{
    // Local\ prefix scopes the mutex to the current user session — no admin needed,
    // and doesn't collide with other Windows users on the same machine.
    private const string MutexName = @"Local\JTC-SingleInstance-yalyoha";

    private static Mutex? _mutex;
    private static FileSystemWatcher? _watcher;
    private static string InboxDir => Path.Combine(AppPaths.Root, "inbox");

    // Sentinel content the installer drops into the inbox before overwriting files,
    // asking the running instance to exit cleanly. The mutex it holds gets released as
    // a side-effect of process exit, which is what the installer's PrepareToInstall
    // hook actually waits on.
    public const string ShutdownMarker = "@shutdown";

    public static event Action<string>? TorrentPathReceived;
    public static event Action<string>? MagnetReceived;
    public static event Action? ShowWindowRequested;
    public static event Action? ShutdownRequested;

    /// <summary>
    /// Attempts to claim the primary-instance mutex. If another instance already holds it,
    /// writes the caller's CLI arguments into the inbox for the primary to pick up, then
    /// returns <c>true</c> — the caller should exit its process immediately.
    /// Returns <c>false</c> if we're the primary and should continue normal startup.
    /// </summary>
    public static bool TryClaimOrHandOff(string[] args)
    {
        var source = ExtractLaunchSource(args);

        _mutex = new Mutex(initiallyOwned: false, MutexName, out bool createdNew);
        if (createdNew)
        {
            // First instance — take ownership and stay running.
            try { _mutex.WaitOne(0); } catch { /* already owned, fine */ }
            return false;
        }

        // Secondary instance — drop a note in the inbox and exit. Empty content means
        // "just bring the primary's window back from the tray"; a torrent path or a
        // magnet URI means "open this in the primary". The primary detects which by
        // checking the "magnet:" prefix.
        try
        {
            Directory.CreateDirectory(InboxDir);
            var target = Path.Combine(InboxDir, Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(target, source ?? string.Empty);
        }
        catch { /* best-effort — user can just add manually */ }
        _mutex.Dispose();
        _mutex = null;
        return true;
    }

    /// <summary>
    /// Called once on the primary instance after the main window is ready. Drains any
    /// pre-existing inbox files, then watches for new drops from later "Open with" launches.
    /// </summary>
    public static void StartWatching()
    {
        Directory.CreateDirectory(InboxDir);
        DrainInbox();
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(InboxDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, _) => DrainInbox();
    }

    private static void DrainInbox()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(InboxDir, "*.txt"))
            {
                string? content = null;
                try { content = File.ReadAllText(file).Trim(); }
                catch { /* file may still be locked by writer; skip and retry next tick */ }

                if (string.IsNullOrEmpty(content))
                    ShowWindowRequested?.Invoke();
                else if (string.Equals(content, ShutdownMarker, StringComparison.OrdinalIgnoreCase))
                    ShutdownRequested?.Invoke();
                else if (content.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    MagnetReceived?.Invoke(content);
                else
                    TorrentPathReceived?.Invoke(content);

                try { File.Delete(file); } catch { /* fine */ }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("SingleInstance.DrainInbox", ex);
        }
    }

    /// <summary>
    /// Extracts either a .torrent file path OR a magnet URI from the process's launch
    /// arguments — whichever comes first. Returns null if neither is present. Callers
    /// distinguish which they got by checking the "magnet:" prefix.
    /// </summary>
    public static string? ExtractLaunchSource(string[] args)
    {
        // args[0] is the exe path when launched via Environment.GetCommandLineArgs().
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i]?.Trim();
            if (string.IsNullOrWhiteSpace(a)) continue;
            if (a.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return a;
            if (a.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                return a;
        }
        return null;
    }
}
