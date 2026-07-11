using MonoTorrent;
using MonoTorrent.Client;

namespace TClient.Services;

public sealed class TorrentService : IAsyncDisposable
{
    // Higher than MonoTorrent's default; more peer slots = better download parallelism.
    // 500 is safe on modern hardware; going higher yields diminishing returns.
    private const int MaxPeerConnections = 500;

    // Faster ramp-up when adding a torrent — more parallel connection attempts.
    private const int MaxHalfOpenConnections = 30;

    private readonly ClientEngine _engine;
    private readonly StateStore _store;
    private readonly Dictionary<TorrentManager, PersistedTorrent> _persistedByManager = new();
    // Serializes add/remove so an Add can never race a still-running Remove.
    // MonoTorrent otherwise throws "A manager for this torrent has already been registered"
    // because its info-hash cleanup lags the RemoveAsync return.
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private bool _disposed;

    public event EventHandler<TorrentManager>? TorrentAdded;
    public event EventHandler<TorrentManager>? TorrentRemoved;

    public IReadOnlyList<TorrentManager> Torrents => (IReadOnlyList<TorrentManager>)_engine.Torrents;

    public TorrentService()
    {
        AppPaths.EnsureExists();
        _store = new StateStore(AppPaths.Root);
        _engine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = AppPaths.CacheDir,
            MaximumConnections = MaxPeerConnections,
            MaximumHalfOpenConnections = MaxHalfOpenConnections,
            // Explicitly on (also defaults, but making intent visible):
            AllowLocalPeerDiscovery = true,      // BEP 14 — LAN peer discovery
            AllowPortForwarding = true,          // UPnP / NAT-PMP — inbound reachability
            AutoSaveLoadDhtCache = true,         // remember DHT nodes across restarts
            AutoSaveLoadFastResume = true,       // skip full re-hash on restart
            AutoSaveLoadMagnetLinkMetadata = true, // cache magnet metadata
        }.ToSettings());
    }

    /// <summary>
    /// Applies user-facing settings that affect torrent-management policy.
    /// Currently: if the download-limit was raised, resume waiting torrents; if lowered,
    /// no-op (we don't preempt running torrents on the fly).
    /// </summary>
    public Task ApplySettingsAsync(AppSettings _)
    {
        return StartNextIfSlotFreeAsync();
    }

    public async Task<TorrentManager> AddTorrentFileAsync(string torrentPath, string downloadDir, bool startImmediately)
    {
        DebugLog.Info($"AddTorrentFileAsync ENTER path='{torrentPath}' start={startImmediately}");
        if (!File.Exists(torrentPath))
            throw new FileNotFoundException("Torrent file not found", torrentPath);
        Directory.CreateDirectory(downloadDir);

        await _mutation.WaitAsync();
        DebugLog.Info("  Add: semaphore acquired");
        try
        {
            if (IsAlreadyTracked(torrentPath))
            {
                DebugLog.Info("  Add: rejected as duplicate");
                throw new InvalidOperationException("Этот торрент уже добавлен.");
            }
            DebugLog.Info($"  Add: engine.Torrents.Count before = {_engine.Torrents.Count}");
            var manager = await AddWithRetryAsync(() => _engine.AddAsync(torrentPath, downloadDir));
            DebugLog.Info($"  Add: engine.AddAsync ok, engine.Torrents.Count after = {_engine.Torrents.Count}");
            WireStateChange(manager);
            _persistedByManager[manager] = new PersistedTorrent
            {
                Source = torrentPath,
                SourceKind = PersistedSourceKind.TorrentFile,
                DownloadDir = downloadDir,
                Paused = !startImmediately,
            };
            TorrentAdded?.Invoke(this, manager);
            if (startImmediately && CanStartMore())
                await manager.StartAsync();
            await SaveStateAsync();
            DebugLog.Info("  Add: done");
            return manager;
        }
        catch (Exception ex) { DebugLog.Error("AddTorrentFileAsync", ex); throw; }
        finally { _mutation.Release(); DebugLog.Info("  Add: semaphore released"); }
    }

    public async Task<TorrentManager> AddMagnetAsync(string magnetUri, string downloadDir, bool startImmediately)
    {
        if (!MagnetLink.TryParse(magnetUri, out var link) || link is null)
            throw new ArgumentException("Invalid magnet link", nameof(magnetUri));
        Directory.CreateDirectory(downloadDir);

        await _mutation.WaitAsync();
        try
        {
            if (IsAlreadyTracked(magnetUri))
                throw new InvalidOperationException("Этот magnet уже добавлен.");

            var manager = await AddWithRetryAsync(() => _engine.AddAsync(link, downloadDir));
            WireStateChange(manager);
            _persistedByManager[manager] = new PersistedTorrent
            {
                Source = magnetUri,
                SourceKind = PersistedSourceKind.Magnet,
                DownloadDir = downloadDir,
                Paused = !startImmediately,
            };
            TorrentAdded?.Invoke(this, manager);
            if (startImmediately && CanStartMore())
                await manager.StartAsync();
            await SaveStateAsync();
            return manager;
        }
        finally { _mutation.Release(); }
    }

    public async Task PauseAsync(TorrentManager manager)
    {
        await manager.PauseAsync();
        if (_persistedByManager.TryGetValue(manager, out var entry))
            _persistedByManager[manager] = entry with { Paused = true };
        await SaveStateAsync();
    }

    public async Task ResumeAsync(TorrentManager manager)
    {
        // Resume respects the user's explicit intent — bypasses the auto-queue limit.
        await manager.StartAsync();
        if (_persistedByManager.TryGetValue(manager, out var entry))
            _persistedByManager[manager] = entry with { Paused = false };
        await SaveStateAsync();
    }

    public async Task RemoveAsync(TorrentManager manager, bool deleteFilesOnDisk)
    {
        DebugLog.Info($"RemoveAsync ENTER name='{manager.Torrent?.Name}' state={manager.State} deleteFiles={deleteFilesOnDisk}");
        var downloadDir = manager.SavePath;
        var torrentName = manager.Torrent?.Name;

        manager.TorrentStateChanged -= OnManagerStateChanged;

        await _mutation.WaitAsync();
        DebugLog.Info("  Remove: semaphore acquired");
        try
        {
            try
            {
                if (manager.State != TorrentState.Stopped)
                {
                    DebugLog.Info($"  Remove: StopAsync(2s timeout) (from state={manager.State})");
                    // MonoTorrent's default StopAsync waits for tracker "stopped" announces to reply,
                    // which can be 30-60 seconds on flaky trackers. 2s is enough to try one round.
                    try { await manager.StopAsync(TimeSpan.FromSeconds(2)); }
                    catch (Exception ex) { DebugLog.Error("StopAsync", ex); }
                    DebugLog.Info($"  Remove: after StopAsync, state={manager.State}");
                }
                DebugLog.Info($"  Remove: engine.RemoveAsync start, engine.Torrents.Count={_engine.Torrents.Count}");
                var removed = await _engine.RemoveAsync(manager);
                DebugLog.Info($"  Remove: engine.RemoveAsync returned {removed}, engine.Torrents.Count={_engine.Torrents.Count}");

                // MonoTorrent's RemoveAsync doesn't guarantee full internal eviction on return —
                // Add of the same torrent immediately after can still throw "already registered".
                // Poll until the manager reference is truly gone from engine's tracking, up to 15s.
                var deadline = DateTime.UtcNow.AddSeconds(15);
                var polls = 0;
                while (_engine.Torrents.Any(t => ReferenceEquals(t, manager)) && DateTime.UtcNow < deadline)
                {
                    polls++;
                    await Task.Delay(100);
                }
                DebugLog.Info($"  Remove: manager-reference eviction confirmed after {polls} polls");
                // Extra safety buffer for MonoTorrent's async info-hash tracking cleanup.
                await Task.Delay(500);
                DebugLog.Info("  Remove: buffer wait done");
            }
            finally
            {
                _persistedByManager.Remove(manager);
                TorrentRemoved?.Invoke(this, manager);
                try { await SaveStateAsync(); }
                catch (Exception ex) { DebugLog.Error("SaveStateAsync in remove finally", ex); }
            }
        }
        catch (Exception ex) { DebugLog.Error("RemoveAsync", ex); throw; }
        finally { _mutation.Release(); DebugLog.Info("  Remove: semaphore released"); }

        if (deleteFilesOnDisk)
        {
            await DeleteTorrentFilesAsync(manager, downloadDir, torrentName);
        }

        // A slot may have just freed up.
        _ = StartNextIfSlotFreeAsync();
    }

    public async Task LoadStateAsync()
    {
        var items = await _store.LoadAsync();
        foreach (var item in items)
        {
            try
            {
                switch (item.SourceKind)
                {
                    case PersistedSourceKind.TorrentFile:
                        if (File.Exists(item.Source))
                            await AddTorrentFileAsync(item.Source, item.DownloadDir, startImmediately: !item.Paused);
                        break;
                    case PersistedSourceKind.Magnet:
                        await AddMagnetAsync(item.Source, item.DownloadDir, startImmediately: !item.Paused);
                        break;
                }
            }
            catch
            {
                // Ignore individual failures; keep loading the rest.
            }
        }
    }

    /// <summary>
    /// Deletes the torrent's data files from disk. Uses MonoTorrent's file list to get
    /// each file's actual path (works for both single- and multi-file torrents), then
    /// removes any now-empty containing directories. Retries a few times because
    /// file handles from the piece writer may not be fully released the moment
    /// engine.RemoveAsync returns.
    /// </summary>
    private static async Task DeleteTorrentFilesAsync(TorrentManager manager, string downloadDir, string? torrentName)
    {
        // Collect all files' full paths BEFORE we release the manager reference.
        var filePaths = new List<string>();
        try
        {
            foreach (var f in manager.Files)
            {
                if (!string.IsNullOrEmpty(f.FullPath))
                    filePaths.Add(f.FullPath);
            }
        }
        catch (Exception ex) { DebugLog.Error("collect file paths", ex); }

        // Fallback for magnet torrents whose metadata never resolved (Files is empty):
        // best guess is downloadDir\torrentName.
        if (filePaths.Count == 0 && !string.IsNullOrEmpty(torrentName))
            filePaths.Add(Path.Combine(downloadDir, torrentName));

        DebugLog.Info($"DeleteTorrentFiles: {filePaths.Count} paths to delete");

        // Try each file with a few retries — piece writer may still be flushing.
        foreach (var path in filePaths)
            await DeletePathWithRetriesAsync(path);

        // Multi-file torrents put everything in a subfolder <downloadDir>\<torrentName>.
        // Remove it (and any empty ancestor folders inside downloadDir).
        if (!string.IsNullOrEmpty(torrentName))
        {
            var container = Path.Combine(downloadDir, torrentName);
            if (Directory.Exists(container))
            {
                // Wipe any leftover empty subfolders + the container itself.
                try { Directory.Delete(container, recursive: true); }
                catch (Exception ex) { DebugLog.Error($"remove container {container}", ex); }
            }
        }
    }

    private static async Task DeletePathWithRetriesAsync(string path)
    {
        // Retry with escalating delays: 0, 200, 400, 800, 1600ms — total ~3s.
        var delays = new[] { 0, 200, 400, 800, 1600 };
        foreach (var delay in delays)
        {
            if (delay > 0) await Task.Delay(delay);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    DebugLog.Info($"  deleted dir: {path}");
                    return;
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                    DebugLog.Info($"  deleted file: {path}");
                    return;
                }
                // Path doesn't exist — nothing to do.
                return;
            }
            catch (IOException) { /* file may be locked, retry */ }
            catch (UnauthorizedAccessException) { /* AV or read-only, retry */ }
            catch (Exception ex)
            {
                DebugLog.Error($"delete {path}", ex);
                return; // unrecoverable, don't retry
            }
        }
        DebugLog.Info($"  gave up deleting: {path}");
    }

    private Task SaveStateAsync() => _store.SaveAsync(_persistedByManager.Values.ToArray());

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var mgr in _engine.Torrents)
            mgr.TorrentStateChanged -= OnManagerStateChanged;
        await _engine.StopAllAsync();
        _engine.Dispose();
    }

    // ---- Simultaneous-download queue -------------------------------------------------

    private void WireStateChange(TorrentManager manager)
    {
        manager.TorrentStateChanged -= OnManagerStateChanged;
        manager.TorrentStateChanged += OnManagerStateChanged;
    }

    private async void OnManagerStateChanged(object? sender, TorrentStateChangedEventArgs e)
    {
        try
        {
            // A slot frees when a manager stops downloading (finished → Seeding, or externally paused/stopped/errored).
            if (WasDownloading(e.OldState) && !WasDownloading(e.NewState))
                await StartNextIfSlotFreeAsync();
        }
        catch { /* best-effort */ }
    }

    private async Task StartNextIfSlotFreeAsync()
    {
        // Start "wanted" torrents (Paused=false in our record) that aren't currently downloading,
        // until we hit the configured limit.
        while (CanStartMore())
        {
            TorrentManager? next = null;
            foreach (var kvp in _persistedByManager)
            {
                if (kvp.Value.Paused) continue;
                if (WasDownloading(kvp.Key.State)) continue;
                if (kvp.Key.State == TorrentState.Seeding) continue; // already done
                next = kvp.Key;
                break;
            }
            if (next is null) return;
            try { await next.StartAsync(); }
            catch { return; /* stop trying if start fails */ }
        }
    }

    private bool CanStartMore()
    {
        var settings = SettingsStore.Load();
        var active = 0;
        foreach (var mgr in _engine.Torrents)
        {
            if (WasDownloading(mgr.State)) active++;
        }
        return active < settings.MaxSimultaneousDownloads;
    }

    private static bool WasDownloading(TorrentState state) => state is
        TorrentState.Starting
        or TorrentState.Downloading
        or TorrentState.Hashing
        or TorrentState.Metadata
        or TorrentState.FetchingHashes;

    // MonoTorrent's engine.AddAsync can throw briefly after a previous RemoveAsync because
    // internal cleanup (fastresume flush, hash-table eviction, etc.) is asynchronous. The
    // exception message is "A manager for this torrent has already been registered".
    // Retry with escalating backoff before surfacing the error to the user. We match by
    // message rather than exception type so a future MonoTorrent refactor won't break us.
    // Real errors (corrupt file, disk permissions, invalid magnet) don't match this
    // message and surface on the first attempt.
    private bool IsAlreadyTracked(string source) =>
        _persistedByManager.Values.Any(v =>
            string.Equals(v.Source, source, StringComparison.OrdinalIgnoreCase));

    // Short retry loop for the race window between engine.RemoveAsync completing and
    // MonoTorrent's async info-hash cleanup finishing. Duplicates are caught by
    // IsAlreadyTracked() upstream, so anything reaching here is a genuine race.
    private static async Task<TorrentManager> AddWithRetryAsync(Func<Task<TorrentManager>> add)
    {
        const int maxAttempts = 4;
        for (int i = 1; i < maxAttempts; i++)
        {
            try { return await add(); }
            catch (Exception ex) when (IsAlreadyRegistered(ex))
            {
                await Task.Delay(250 * i); // 250, 500, 750 — total ~1.5s
            }
        }
        return await add(); // final attempt: surface any remaining exception to the caller
    }

    private static bool IsAlreadyRegistered(Exception ex) =>
        ex.Message.Contains("already been registered", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase);
}
