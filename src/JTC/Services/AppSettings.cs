using System.Text.Json.Serialization;

namespace JTC.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    // "Фирменная 1" — original pink→orange gradient. Enum name kept as `Brand`
    // (not `Brand1`) so existing settings.json values continue to deserialize.
    Brand,
    Dark,
    Light,
    // "Фирменная 2" — cool blue-navy → lime-green gradient (rgb(50,65,102) →
    // rgb(122,179,23)). Same white plashkas as Brand.
    Brand2,
}

public sealed record AppSettings
{
    public string? LastDownloadDir { get; init; }
    public int MaxSimultaneousDownloads { get; init; } = 3;
    public AppTheme Theme { get; init; } = AppTheme.Brand;
}
