using System.Text.Json;

namespace TClient.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = FilePath + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, settings, Options);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
