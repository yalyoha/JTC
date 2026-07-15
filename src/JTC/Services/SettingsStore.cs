using System.Text.Json;

namespace JTC.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Global lock. SettingsStore is static (unlike StateStore) so we serialise across all
    // callers in the process. Save is rare (only user-visible settings changes) — a lock
    // is fine and matches StateStore's "one writer at a time" invariant.
    private static readonly object _writeLock = new();

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
        lock (_writeLock)
        {
            // Unique per-write temp — see StateStore.SaveAsync for the rationale.
            var tmp = Path.Combine(AppPaths.Root, Path.GetRandomFileName() + ".tmp");
            try
            {
                using (var stream = File.Create(tmp))
                {
                    JsonSerializer.Serialize(stream, settings, Options);
                }
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }
    }
}
