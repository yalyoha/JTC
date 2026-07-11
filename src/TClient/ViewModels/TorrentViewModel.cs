using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using TClient.Helpers;
using Windows.UI;

namespace TClient.ViewModels;

public sealed partial class TorrentViewModel : ObservableObject
{
    // Cached per-state row-background brushes. All semi-transparent so the gradient
    // window backdrop still shows through — colors sit inside the pink→orange palette
    // without clashing. Alphas kept low (~15-25%) so the pink upper part of the gradient
    // doesn't blow rows out to pure white.
    private static readonly SolidColorBrush BrushSeeding     = new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)); // white — done
    private static readonly SolidColorBrush BrushDownloading = new(Color.FromArgb(0x40, 0xFF, 0xD5, 0x80)); // warm amber — active
    private static readonly SolidColorBrush BrushChecking    = new(Color.FromArgb(0x40, 0xFF, 0x8A, 0xD5)); // magenta — hashing/metadata
    private static readonly SolidColorBrush BrushIdle        = new(Color.FromArgb(0x35, 0x30, 0x20, 0x40)); // deep plum — paused/stopped
    private static readonly SolidColorBrush BrushError       = new(Color.FromArgb(0x50, 0xB0, 0x20, 0x20)); // dark red — error
    private static readonly SolidColorBrush BrushTransparent = new(Colors.Transparent);

    private readonly DispatcherQueue _dispatcher;

    public TorrentManager Manager { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private double _progress;         // 0..100
    [ObservableProperty] private string _progressText = "0.0%";
    [ObservableProperty] private string _downloadRateText = "—";
    [ObservableProperty] private string _uploadRateText = "—";
    [ObservableProperty] private int _peerCount;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private Brush _rowBackground = BrushTransparent;

    public TorrentViewModel(TorrentManager manager, DispatcherQueue dispatcher)
    {
        Manager = manager;
        _dispatcher = dispatcher;

        Name = manager.Torrent?.Name ?? "(загрузка метаданных…)";
        SizeText = manager.Torrent is null ? "—" : Formatting.BytesToHuman(manager.Torrent.Size);
        StateText = Formatting.StateToRu(manager.State);
        RowBackground = BrushForState(manager.State);

        manager.TorrentStateChanged += OnStateChanged;
    }

    public void Refresh()
    {
        // Called on the UI thread by MainViewModel's timer.
        if (Manager.Torrent is not null && SizeText == "—")
        {
            Name = Manager.Torrent.Name;
            SizeText = Formatting.BytesToHuman(Manager.Torrent.Size);
        }

        Progress = Manager.Progress;
        ProgressText = $"{Manager.Progress:F1}%";
        DownloadRateText = Formatting.RateToHuman(Manager.Monitor.DownloadRate);
        UploadRateText = Formatting.RateToHuman(Manager.Monitor.UploadRate);
        PeerCount = Manager.Peers.Available;
        // "Ожидание" reads better than "Скачивание" when we haven't found any peers yet —
        // the torrent hasn't actually stalled, it just has nobody to talk to.
        StateText = Manager.State == TorrentState.Downloading && Manager.Peers.Available == 0
            ? "Ожидание"
            : Formatting.StateToRu(Manager.State);
        IsPaused = Manager.State is TorrentState.Paused or TorrentState.Stopped;
        RowBackground = BrushForState(Manager.State);
    }

    private void OnStateChanged(object? sender, TorrentStateChangedEventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            StateText = Formatting.StateToRu(e.NewState);
            IsPaused = e.NewState is TorrentState.Paused or TorrentState.Stopped;
            RowBackground = BrushForState(e.NewState);
        });
    }

    public void Detach()
    {
        Manager.TorrentStateChanged -= OnStateChanged;
    }

    private static SolidColorBrush BrushForState(TorrentState state) => state switch
    {
        TorrentState.Seeding                                                => BrushSeeding,
        TorrentState.Downloading or TorrentState.Starting                   => BrushDownloading,
        TorrentState.Hashing or TorrentState.HashingPaused
            or TorrentState.Metadata or TorrentState.FetchingHashes         => BrushChecking,
        TorrentState.Paused or TorrentState.Stopped or TorrentState.Stopping => BrushIdle,
        TorrentState.Error                                                  => BrushError,
        _                                                                    => BrushTransparent,
    };
}
