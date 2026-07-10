namespace TClient.Services;

public enum PersistedSourceKind
{
    TorrentFile,
    Magnet,
}

public sealed class PersistedTorrent
{
    public string Source { get; init; } = "";
    public PersistedSourceKind SourceKind { get; init; }
    public string DownloadDir { get; init; } = "";
    public bool Paused { get; init; }
}
