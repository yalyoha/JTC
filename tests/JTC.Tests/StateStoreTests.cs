using JTC.Services;

namespace JTC.Tests;

public class StateStoreTests : IDisposable
{
    private readonly string _tempDir;

    public StateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JTCTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var store = new StateStore(_tempDir);
        var loaded = await store.LoadAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var store = new StateStore(_tempDir);
        var items = new List<PersistedTorrent>
        {
            new()
            {
                Source = @"C:\downloads\ubuntu.torrent",
                SourceKind = PersistedSourceKind.TorrentFile,
                DownloadDir = @"D:\Downloads",
                Paused = false,
            },
            new()
            {
                Source = "magnet:?xt=urn:btih:ABCDEF",
                SourceKind = PersistedSourceKind.Magnet,
                DownloadDir = @"D:\Downloads",
                Paused = true,
            },
        };

        await store.SaveAsync(items);
        var loaded = await store.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(items[0].Source, loaded[0].Source);
        Assert.Equal(items[0].SourceKind, loaded[0].SourceKind);
        Assert.Equal(items[0].DownloadDir, loaded[0].DownloadDir);
        Assert.Equal(items[0].Paused, loaded[0].Paused);
        Assert.Equal(items[1].Source, loaded[1].Source);
        Assert.Equal(items[1].SourceKind, loaded[1].SourceKind);
        Assert.Equal(items[1].Paused, loaded[1].Paused);
    }

    [Fact]
    public async Task SaveAsync_EmptyList_WritesEmptyArray()
    {
        var store = new StateStore(_tempDir);
        await store.SaveAsync([]);
        var loaded = await store.LoadAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyAndDoesNotThrow()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "torrents.json"), "this is not json {");
        var store = new StateStore(_tempDir);
        var loaded = await store.LoadAsync();
        Assert.Empty(loaded);
    }
}
