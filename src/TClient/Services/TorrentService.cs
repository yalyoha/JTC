using MonoTorrent;
using MonoTorrent.Client;

namespace TClient.Services;

public sealed class TorrentService : IAsyncDisposable
{
    private readonly ClientEngine _engine;
    private readonly StateStore _store;
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
        TorrentAdded?.Invoke(this, manager);
        if (startImmediately)
            await manager.StartAsync();
        return manager;
    }

    public async Task<TorrentManager> AddMagnetAsync(string magnetUri, string downloadDir, bool startImmediately)
    {
        if (!MagnetLink.TryParse(magnetUri, out var link) || link is null)
            throw new ArgumentException("Invalid magnet link", nameof(magnetUri));
        Directory.CreateDirectory(downloadDir);

        var manager = await _engine.AddAsync(link, downloadDir);
        TorrentAdded?.Invoke(this, manager);
        if (startImmediately)
            await manager.StartAsync();
        return manager;
    }

    public Task PauseAsync(TorrentManager manager) => manager.PauseAsync();

    public Task ResumeAsync(TorrentManager manager) => manager.StartAsync();

    public async Task RemoveAsync(TorrentManager manager, bool deleteFilesOnDisk)
    {
        var downloadDir = manager.SavePath;
        var torrentName = manager.Torrent?.Name;

        await manager.StopAsync();
        await _engine.RemoveAsync(manager);
        TorrentRemoved?.Invoke(this, manager);

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _engine.StopAllAsync();
        _engine.Dispose();
    }
}
