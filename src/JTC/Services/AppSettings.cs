namespace JTC.Services;

public sealed record AppSettings
{
    public string? LastDownloadDir { get; init; }
    public int MaxSimultaneousDownloads { get; init; } = 3;
}
