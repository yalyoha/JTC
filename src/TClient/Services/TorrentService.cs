using MonoTorrent;
using MonoTorrent.Client;

namespace TClient.Services;

public sealed class TorrentService : IAsyncDisposable
{
    private readonly ClientEngine _engine;
    private readonly StateStore _store;
    private readonly Dictionary<TorrentManager, PersistedTorrent> _persistedByManager = new();
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
            MaximumConnections = 200,
        }.ToSettings());
    }

    public async Task<TorrentManager> AddTorrentFileAsync(string torrentPath, string downloadDir, bool startImmediately)
    {
        if (!File.Exists(torrentPath))
            throw new FileNotFoundException("Torrent file not found", torrentPath);
        Directory.CreateDirectory(downloadDir);

        var manager = await _engine.AddAsync(torrentPath, downloadDir);
        _persistedByManager[manager] = new PersistedTorrent
        {
            Source = torrentPath,
            SourceKind = PersistedSourceKind.TorrentFile,
            DownloadDir = downloadDir,
            Paused = !startImmediately,
        };
        TorrentAdded?.Invoke(this, manager);
        if (startImmediately)
            await manager.StartAsync();
        await SaveStateAsync();
        return manager;
    }

    public async Task<TorrentManager> AddMagnetAsync(string magnetUri, string downloadDir, bool startImmediately)
    {
        if (!MagnetLink.TryParse(magnetUri, out var link) || link is null)
            throw new ArgumentException("Invalid magnet link", nameof(magnetUri));
        Directory.CreateDirectory(downloadDir);

        var manager = await _engine.AddAsync(link, downloadDir);
        _persistedByManager[manager] = new PersistedTorrent
        {
            Source = magnetUri,
            SourceKind = PersistedSourceKind.Magnet,
            DownloadDir = downloadDir,
            Paused = !startImmediately,
        };
        TorrentAdded?.Invoke(this, manager);
        if (startImmediately)
            await manager.StartAsync();
        await SaveStateAsync();
        return manager;
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
        await manager.StartAsync();
        if (_persistedByManager.TryGetValue(manager, out var entry))
            _persistedByManager[manager] = entry with { Paused = false };
        await SaveStateAsync();
    }

    public async Task RemoveAsync(TorrentManager manager, bool deleteFilesOnDisk)
    {
        var downloadDir = manager.SavePath;
        var torrentName = manager.Torrent?.Name;

        await manager.StopAsync();
        await _engine.RemoveAsync(manager);
        _persistedByManager.Remove(manager);
        TorrentRemoved?.Invoke(this, manager);
        await SaveStateAsync();

        if (deleteFilesOnDisk && !string.IsNullOrEmpty(torrentName))
        {
            // Torrent contents are inside downloadDir. Delete the torrent's files/folder specifically,
            // never the whole downloadDir (which might contain unrelated files).
            var target = Path.Combine(downloadDir, torrentName);
            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                else if (File.Exists(target))
                    File.Delete(target);
            }
            catch (IOException)
            {
                // File may be held by an antivirus scanner; user can delete manually.
            }
        }
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
                        // Source file gone: silently skip. User can re-add.
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

    private Task SaveStateAsync() => _store.SaveAsync(_persistedByManager.Values.ToArray());

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _engine.StopAllAsync();
        _engine.Dispose();
    }
}
