using System.Text.Json.Serialization;

namespace JTC.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    Brand,
    Dark,
    Light,
}

public sealed record AppSettings
{
    public string? LastDownloadDir { get; init; }
    public int MaxSimultaneousDownloads { get; init; } = 3;
    public AppTheme Theme { get; init; } = AppTheme.Brand;
}
