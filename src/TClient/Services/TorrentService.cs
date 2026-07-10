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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _engine.StopAllAsync();
        _engine.Dispose();
    }
}
