using System.Text.Json;
using System.Text.Json.Serialization;

namespace TClient.Services;

public sealed class StateStore
{
    private const string FileName = "torrents.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public StateStore(string directory)
    {
        _directory = directory;
    }

    private string FilePath => Path.Combine(_directory, FileName);

    public async Task<List<PersistedTorrent>> LoadAsync()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var result = await JsonSerializer.DeserializeAsync<List<PersistedTorrent>>(stream, Options);
            return result ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<PersistedTorrent> items)
    {
        Directory.CreateDirectory(_directory);
        var tmp = FilePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, items, Options);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
