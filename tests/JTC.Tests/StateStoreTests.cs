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

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSkipFileIndices()
    {
        var store = new StateStore(_tempDir);
        var items = new List<PersistedTorrent>
        {
            new()
            {
                Source = @"C:\downloads\pack.torrent",
                SourceKind = PersistedSourceKind.TorrentFile,
                DownloadDir = @"D:\Downloads",
                Paused = false,
                SkipFileIndices = new[] { 0, 3, 7 },
            },
        };

        await store.SaveAsync(items);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.NotNull(loaded[0].SkipFileIndices);
        Assert.Equal(new[] { 0, 3, 7 }, loaded[0].SkipFileIndices);
    }

    [Fact]
    public async Task LoadAsync_LegacyRecordWithoutSkipFileIndices_LoadsWithNull()
    {
        // Regression: torrents.json written by pre-task-7 builds has no "SkipFileIndices"
        // field. Deserialisation must not throw and must yield SkipFileIndices == null so
        // legacy records default to "download everything".
        const string legacyJson = """
        [
          {
            "Source": "C:/downloads/old.torrent",
            "SourceKind": "TorrentFile",
            "DownloadDir": "D:/Downloads",
            "Paused": false
          }
        ]
        """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "torrents.json"), legacyJson);
        var store = new StateStore(_tempDir);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.Null(loaded[0].SkipFileIndices);
        Assert.Equal("C:/downloads/old.torrent", loaded[0].Source);
    }

    [Fact]
    public async Task SaveAsync_ManyParallelWriters_DoNotThrowAndLeaveValidJson()
    {
        // Regression: without the SemaphoreSlim + unique temp file, overlapping writers
        // used to race on the shared "torrents.json.tmp" path and throw IOException, or
        // (worse) leave a torn file behind. This spawns a burst of concurrent writes,
        // each with different content, and checks that (a) none throws, and (b) the
        // final on-disk state parses back to *one of* the written payloads intact.
        var store = new StateStore(_tempDir);
        var writers = Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var items = new List<PersistedTorrent>
            {
                new()
                {
                    Source = $"C:/dl/writer_{i}.torrent",
                    SourceKind = PersistedSourceKind.TorrentFile,
                    DownloadDir = $@"D:\Downloads\writer_{i}",
                    Paused = i % 2 == 0,
                },
            };
            await store.SaveAsync(items);
        })).ToArray();

        await Task.WhenAll(writers);

        var loaded = await store.LoadAsync();
        Assert.Single(loaded);
        Assert.StartsWith("C:/dl/writer_", loaded[0].Source);
        Assert.EndsWith(".torrent", loaded[0].Source);

        // And no stray temp files left behind (cleanup happens either in the atomic Move
        // or in SaveAsync's error-path try/catch).
        var strayTemps = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(strayTemps);
    }
}
