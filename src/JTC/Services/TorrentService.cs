using System.Net;
using JTC.Helpers;
using MonoTorrent;
using MonoTorrent.Client;

namespace JTC.Services;

public sealed class TorrentService : IAsyncDisposable
{
    // Higher than MonoTorrent's default; more peer slots = better download parallelism.
    // 500 is safe on modern hardware; going higher yields diminishing returns.
    private const int MaxPeerConnections = 500;

    // Faster ramp-up when adding a torrent — more parallel connection attempts.
    // Raised from 30 → 50 because the typical usage pattern here is ONE hot torrent
    // at a time; all half-open budget goes to it, so more parallel attempts = faster
    // pool build-up = faster time-to-peak-speed.
    private const int MaxHalfOpenConnections = 50;

    // Fixed TCP+UDP port so UPnP/NAT-PMP mappings survive restarts and known peers
    // can reconnect to the same address. 51413 is the qBittorrent default — well-known,
    // not conflicting with other common apps.
    private const int PeerListenPort = 51413;

    // Per-torrent connection cap. MonoTorrent's default per-torrent limit is much lower
    // than the engine-wide MaximumConnections=500 above, so a single hot torrent leaves
    // most of the global peer budget idle. 200 lets one torrent claim a large share
    // without saturating Windows' half-open TCP limit (~50) or starving a second torrent
    // if the user adds one — two active torrents can still fit under the global 500.
    private const int PerTorrentMaxConnections = 200;

    // Per-torrent upload slots. Raising slots above the MonoTorrent default helps our
    // typical single-torrent workload get better tit-for-tat reciprocity from peers,
    // which improves download throughput. 16 is conservative — high enough to matter
    // on a 200-peer swarm, low enough to leave upload bandwidth for other traffic.
    private const int PerTorrentUploadSlots = 16;

    private readonly ClientEngine _engine;
    private readonly StateStore _store;
    private readonly Dictionary<TorrentManager, PersistedTorrent> _persistedByManager = new();
    // Serializes add/remove so an Add can never race a still-running Remove.
    // MonoTorrent otherwise throws "A manager for this torrent has already been registered"
    // because its info-hash cleanup lags the RemoveAsync return.
    private readonly SemaphoreSlim _mutation = new(1, 1);

    // Periodic diagnostic timer — writes one metrics line per active torrent to debug.log
    // every ~10 s. Without this the "download stalls / drops" reports have nothing to
    // work with: the app-side numbers change second-to-second in the UI and are lost.
    // 10 s is a compromise: dense enough to see ramp-up curves after Add, sparse enough
    // that debug.log's 1 MB rotation lasts many hours on a single active torrent.
    private readonly System.Threading.Timer _diagTimer;
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
            // 128 MB in-memory disk cache (up from 50 MB). Under our typical single-hot-
            // torrent workload one torrent gets all 500 peer slots — the cache smooths
            // its bursty piece writes on slower disks (HDDs, SMR drives) and reduces
            // syscall churn. On SSDs it's still helpful for the read-ahead side of
            // ReadsAndWrites when we upload to peers.
            DiskCacheBytes = 128 * 1024 * 1024,
            DiskCachePolicy = MonoTorrent.PieceWriter.CachePolicy.ReadsAndWrites,
            // Encryption preference: encrypted first, plain-text as fallback.
            // Some ISPs / trackers reject purely-plain-text connections.
            AllowedEncryption = new System.Collections.Generic.List<MonoTorrent.Connections.EncryptionType>
            {
                MonoTorrent.Connections.EncryptionType.RC4Full,
                MonoTorrent.Connections.EncryptionType.RC4Header,
                MonoTorrent.Connections.EncryptionType.PlainText,
            },
            // Fixed listen endpoints so UPnP/NAT-PMP maps a stable port across restarts
            // (peers can reconnect to the same address; DHT UDP socket doesn't shift).
            ListenEndPoints = new System.Collections.Generic.Dictionary<string, IPEndPoint>
            {
                { "ipv4", new IPEndPoint(IPAddress.Any, PeerListenPort) },
            },
            DhtEndPoint = new IPEndPoint(IPAddress.Any, PeerListenPort),
            // Note: MonoTorrent 3.0.2 doesn't expose DhtBootstrapRouters — it uses its
            // own default bootstrap list internally. If a newer version exposes it we
            // should re-add explicit routers here as a resilience measure.
            AllowLocalPeerDiscovery = true,      // BEP 14 — LAN peer discovery
            AllowPortForwarding = true,          // UPnP / NAT-PMP — inbound reachability
            AutoSaveLoadDhtCache = true,         // remember DHT nodes across restarts
            AutoSaveLoadFastResume = true,       // skip full re-hash on restart
            AutoSaveLoadMagnetLinkMetadata = true, // cache magnet metadata
        }.ToSettings());

        // First tick after 15 s so we skip the noisy startup burst; subsequent ticks
        // every 10 s. Ticks are read-only property fetches on background thread —
        // never touches _mutation, never awaits, so it can't deadlock or delay Add/Remove.
        _diagTimer = new System.Threading.Timer(
            LogDiagnosticsTick, null,
            dueTime: TimeSpan.FromSeconds(15),
            period: TimeSpan.FromSeconds(10));
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

    public async Task<TorrentManager> AddTorrentFileAsync(
        string torrentPath, string downloadDir, bool startImmediately,
        IReadOnlySet<int>? skipFileIndices = null)
    {
        DebugLog.Info($"AddTorrentFileAsync ENTER path='{torrentPath}' start={startImmediately} skip={skipFileIndices?.Count ?? 0}");
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
            var manager = await AddWithRetryAsync(() => _engine.AddAsync(torrentPath, downloadDir, BuildTorrentSettings()));
            DebugLog.Info($"  Add: engine.AddAsync ok, engine.Torrents.Count after = {_engine.Torrents.Count}");
            WireStateChange(manager);

            // Apply DoNotDownload to unselected files BEFORE StartAsync so the piece picker
            // never touches their pieces. Setting priority after Start is fine too (MonoTorrent
            // re-evaluates on next pick) but pre-Start avoids a brief window where the engine
            // could allocate slots to a file the user doesn't want.
            if (skipFileIndices is { Count: > 0 })
                await ApplySkipFilePrioritiesAsync(manager, skipFileIndices);

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

    private static async Task ApplySkipFilePrioritiesAsync(TorrentManager manager, IReadOnlySet<int> skipIndices)
    {
        var files = manager.Files;
        if (files is null) return;
        var count = 0;
        for (int i = 0; i < files.Count; i++)
        {
            if (!skipIndices.Contains(i)) continue;
            try
            {
                await manager.SetFilePriorityAsync(files[i], Priority.DoNotDownload);
                count++;
            }
            catch (Exception ex) { DebugLog.Error($"skip file [{i}] {files[i].Path}", ex); }
        }
        DebugLog.Info($"  Add: marked {count}/{files.Count} files as DoNotDownload");
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

            var manager = await AddWithRetryAsync(() => _engine.AddAsync(link, downloadDir, BuildTorrentSettings()));
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

    /// <summary>
    /// Force-rehash every piece of the torrent from disk. Used by the row context-menu
    /// "Обновить" action — helpful when files have been changed externally or when
    /// fast-resume data is suspected to be stale. Auto-resumes downloading if the
    /// torrent was running before the check.
    /// </summary>
    public async Task RecheckAsync(TorrentManager manager)
    {
        var name = manager.Torrent?.Name ?? "(no metadata)";
        var startState = manager.State;
        DebugLog.Info($"RecheckAsync ENTER name='{name}' state={startState}");

        var wasRunning = startState is not TorrentState.Stopped
                                    and not TorrentState.Paused
                                    and not TorrentState.Error;

        // HashCheckAsync requires the manager to be in Stopped state. StopAsync with
        // a 2s timeout matches what RemoveAsync uses — enough for one tracker round,
        // but doesn't wall for flaky trackers.
        try
        {
            DebugLog.Info($"  Recheck: StopAsync (from state={manager.State})");
            await manager.StopAsync(TimeSpan.FromSeconds(2));
            DebugLog.Info($"  Recheck: stopped, state={manager.State}");
        }
        catch (Exception ex) { DebugLog.Error("Recheck.Stop", ex); }

        DebugLog.Info($"  Recheck: HashCheckAsync(autoStart={wasRunning}) begin");
        try
        {
            await manager.HashCheckAsync(autoStart: wasRunning);
            DebugLog.Info($"  Recheck: HashCheckAsync done, state={manager.State}, progress={manager.Progress:F1}%");
        }
        catch (Exception ex)
        {
            DebugLog.Error("Recheck.HashCheckAsync", ex);
            throw;
        }
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
        await _diagTimer.DisposeAsync();
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
        // Diagnostic: capture Error-state transitions with the underlying cause so post-mortem
        // has a concrete exception to look at instead of just "torrent went red". Done outside
        // the try/catch below so a logging bug can't silently be swallowed.
        if (e.NewState == TorrentState.Error && sender is TorrentManager errored)
        {
            var name = errored.Torrent?.Name ?? "(no metadata)";
            var ex = errored.Error?.Exception;
            if (ex is not null)
                DebugLog.Error($"torrent -> Error '{name}'", ex);
            else
                DebugLog.Info($"torrent -> Error '{name}' (no manager.Error captured)");
        }

        try
        {
            // A slot frees when a manager stops downloading (finished → Seeding, or externally paused/stopped/errored).
            if (WasDownloading(e.OldState) && !WasDownloading(e.NewState))
                await StartNextIfSlotFreeAsync();
        }
        catch (Exception ex)
        {
            // Was silently swallowed before task 4. Log so we can see why StartNextIfSlotFreeAsync failed.
            DebugLog.Error("OnManagerStateChanged.StartNextIfSlotFreeAsync", ex);
        }
    }

    // Timer tick: emit one debug.log line per manager currently doing anything interesting.
    // Runs on a ThreadPool thread, off the mutation semaphore path. Each property access is
    // best-effort — MonoTorrent may throw briefly during teardown, so a per-torrent try/catch
    // logs the failure and moves on rather than killing the whole tick.
    private void LogDiagnosticsTick(object? _)
    {
        if (_disposed) return;
        try
        {
            // Snapshot to avoid enumerating engine.Torrents while add/remove mutates it.
            var snapshot = _engine.Torrents.ToArray();
            foreach (var m in snapshot)
            {
                try { LogOneTorrentDiagnostics(m); }
                catch (Exception ex) { DebugLog.Error("diag one-torrent", ex); }
            }
        }
        catch (Exception ex) { DebugLog.Error("diag tick", ex); }
    }

    private void LogOneTorrentDiagnostics(TorrentManager m)
    {
        var state = m.State;
        // Skip fully-idle torrents (Stopped/Paused) — no useful metrics, only log noise.
        // Error state is logged separately by OnManagerStateChanged when it flips there,
        // but we still emit a periodic line so we can see how long it's been stuck.
        if (state is TorrentState.Stopped or TorrentState.Paused)
            return;

        var name = ShortName(m.Torrent?.Name);
        var down = m.Monitor.DownloadRate;
        var up = m.Monitor.UploadRate;
        var progress = m.Progress;

        // Peers.Available = known-but-not-currently-connected peers from tracker/DHT/PeX.
        // OpenConnections = TCP sockets currently established. Both matter: connections
        // without a peer pool = we can't grow; peer pool without connections = we're not
        // making outbound attempts (half-open cap? blocked? firewall?).
        int available = -1, seeds = -1, leechs = -1, open = -1;
        try { available = m.Peers.Available; } catch { }
        try { seeds     = m.Peers.Seeds;     } catch { }
        try { leechs    = m.Peers.Leechs;    } catch { }
        try { open      = m.OpenConnections; } catch { }

        var dhtState = "?";
        try { dhtState = _engine.Dht.State.ToString(); } catch { }

        // Trackers are optional (magnet-only pre-metadata has none). Summarise as
        // "OK/total" counting tiers with at least one working tracker — anything more
        // detailed would blow up the log for public torrents with 30+ trackers.
        int trackerTiers = -1, trackerOk = -1;
        try
        {
            var tiers = m.TrackerManager?.Tiers;
            if (tiers is not null)
            {
                trackerTiers = tiers.Count;
                trackerOk = 0;
                foreach (var tier in tiers)
                {
                    try
                    {
                        if (tier.ActiveTracker?.Status == MonoTorrent.Trackers.TrackerState.Ok)
                            trackerOk++;
                    }
                    catch { }
                }
            }
        }
        catch { }

        DebugLog.Info(
            $"DIAG '{name}' state={state} prog={progress:F1}% " +
            $"D={Formatting.RateToHuman(down)} U={Formatting.RateToHuman(up)} " +
            $"conn={open} peers(avail/seeds/leech)={available}/{seeds}/{leechs} " +
            $"trackers={trackerOk}/{trackerTiers} dht={dhtState}");
    }

    private static string ShortName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "(no metadata)";
        return name.Length <= 60 ? name : name.Substring(0, 57) + "…";
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

    // Per-torrent settings passed to _engine.AddAsync. Without an explicit settings
    // argument the engine assigns MonoTorrent's default per-torrent connection cap,
    // which is far below our engine-wide MaximumConnections=500 — so a single hot
    // torrent used to stall long before it approached the global budget. See the
    // PerTorrentMaxConnections / PerTorrentUploadSlots constants at the top for why
    // these numbers were chosen. DHT and PeX are pinned true explicitly so future
    // MonoTorrent default flips can't silently disable peer discovery on us.
    private static TorrentSettings BuildTorrentSettings() =>
        new TorrentSettingsBuilder
        {
            MaximumConnections = PerTorrentMaxConnections,
            UploadSlots = PerTorrentUploadSlots,
            AllowDht = true,
            AllowPeerExchange = true,
        }.ToSettings();
}
