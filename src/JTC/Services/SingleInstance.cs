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

    public static event Action<string>? TorrentPathReceived;

    /// <summary>
    /// Attempts to claim the primary-instance mutex. If another instance already holds it,
    /// writes the caller's CLI arguments into the inbox for the primary to pick up, then
    /// returns <c>true</c> — the caller should exit its process immediately.
    /// Returns <c>false</c> if we're the primary and should continue normal startup.
    /// </summary>
    public static bool TryClaimOrHandOff(string[] args)
    {
        var torrentPath = ExtractTorrentPath(args);

        _mutex = new Mutex(initiallyOwned: false, MutexName, out bool createdNew);
        if (createdNew)
        {
            // First instance — take ownership and stay running.
            try { _mutex.WaitOne(0); } catch { /* already owned, fine */ }
            return false;
        }

        // Secondary instance — hand off to the primary and exit.
        if (torrentPath is not null)
        {
            try
            {
                Directory.CreateDirectory(InboxDir);
                var target = Path.Combine(InboxDir, Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(target, torrentPath);
            }
            catch { /* best-effort — user can just add manually */ }
        }
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
                string? path = null;
                try { path = File.ReadAllText(file).Trim(); }
                catch { /* file may still be locked by writer; skip and retry next tick */ }

                if (!string.IsNullOrEmpty(path))
                    TorrentPathReceived?.Invoke(path);

                try { File.Delete(file); } catch { /* fine */ }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("SingleInstance.DrainInbox", ex);
        }
    }

    public static string? ExtractTorrentPath(string[] args)
    {
        // args[0] is the exe path when launched via Environment.GetCommandLineArgs().
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!string.IsNullOrWhiteSpace(a) &&
                a.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(a))
                return a;
        }
        return null;
    }
}
