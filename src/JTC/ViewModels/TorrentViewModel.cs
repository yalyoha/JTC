using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using JTC.Helpers;
using Windows.UI;

namespace JTC.ViewModels;

public sealed partial class TorrentViewModel : ObservableObject
{
    // Cached per-state row-background brushes. All semi-transparent so the gradient
    // window backdrop still shows through — colors sit inside the pink→orange palette
    // without clashing.
    //
    // Two variants per state: normal and "selected". The selected variant has ~2x the
    // alpha so selection reads clearly, while still keeping the palette. Because our own
    // Grid draws the background, the highlight matches the row's corner radius exactly
    // (the container's built-in selection background is turned off in MainWindow.xaml).
    private static readonly SolidColorBrush BrushSeeding             = new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrushSeedingSelected     = new(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrushDownloading         = new(Color.FromArgb(0x40, 0xFF, 0xD5, 0x80));
    private static readonly SolidColorBrush BrushDownloadingSelected = new(Color.FromArgb(0x75, 0xFF, 0xD5, 0x80));
    private static readonly SolidColorBrush BrushIdle                = new(Color.FromArgb(0x35, 0x30, 0x20, 0x40));
    private static readonly SolidColorBrush BrushIdleSelected        = new(Color.FromArgb(0x70, 0x30, 0x20, 0x40));
    private static readonly SolidColorBrush BrushError               = new(Color.FromArgb(0x50, 0xB0, 0x20, 0x20));
    private static readonly SolidColorBrush BrushErrorSelected       = new(Color.FromArgb(0x80, 0xB0, 0x20, 0x20));

    // Simplified user-visible state. Everything the engine reports (Paused, Stopped,
    // Starting, Hashing, Metadata, Downloading-with-zero-rate, …) collapses to Waiting;
    // Downloading with a live rate is the only "actively pulling bytes" state.
    private enum Display { Waiting, Downloading, Seeding, Error }

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
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Brush _rowBackground = BrushIdle;

    partial void OnIsSelectedChanged(bool value) => ApplyDisplay(ComputeDisplay(Manager));

    public TorrentViewModel(TorrentManager manager, DispatcherQueue dispatcher)
    {
        Manager = manager;
        _dispatcher = dispatcher;

        Name = manager.Torrent?.Name ?? "(загрузка метаданных…)";
        SizeText = manager.Torrent is null ? "—" : Formatting.BytesToHuman(manager.Torrent.Size);
        ApplyDisplay(ComputeDisplay(manager));

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
        IsPaused = Manager.State is TorrentState.Paused or TorrentState.Stopped;
        ApplyDisplay(ComputeDisplay(Manager));
    }

    private void OnStateChanged(object? sender, TorrentStateChangedEventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            IsPaused = e.NewState is TorrentState.Paused or TorrentState.Stopped;
            ApplyDisplay(ComputeDisplay(Manager));
        });
    }

    public void Detach()
    {
        Manager.TorrentStateChanged -= OnStateChanged;
    }

    private void ApplyDisplay(Display d)
    {
        StateText = d switch
        {
            Display.Seeding     => "Раздача",
            Display.Downloading => "Загрузка",
            Display.Error       => "Ошибка",
            _                   => "Ожидание",
        };
        RowBackground = d switch
        {
            Display.Seeding     => IsSelected ? BrushSeedingSelected     : BrushSeeding,
            Display.Downloading => IsSelected ? BrushDownloadingSelected : BrushDownloading,
            Display.Error       => IsSelected ? BrushErrorSelected       : BrushError,
            _                   => IsSelected ? BrushIdleSelected        : BrushIdle,
        };
    }

    private static Display ComputeDisplay(TorrentManager m) => m.State switch
    {
        TorrentState.Seeding                                     => Display.Seeding,
        TorrentState.Error                                       => Display.Error,
        TorrentState.Downloading when m.Monitor.DownloadRate > 0 => Display.Downloading,
        _                                                        => Display.Waiting,
    };
}
