using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using JTC.Helpers;

namespace JTC.ViewModels;

public sealed partial class TorrentViewModel : ObservableObject
{
    // Per-state row-background brushes come from RowBrushes.Current, which
    // switches with the app theme. All variants are semi-transparent so the
    // theme background still reads through. Because our own Grid draws the
    // background, the highlight matches the row's corner radius exactly (the
    // container's built-in selection background is turned off in MainWindow.xaml).

    // Simplified user-visible state. Everything the engine reports (Paused, Stopped,
    // Starting, Metadata, Downloading-with-zero-rate, …) collapses to Waiting; Downloading
    // with a live rate is the only "actively pulling bytes" state. Hashing is broken out
    // separately so the user can see rechecks in progress after "Обновить" — otherwise a
    // multi-minute hash check just shows as "Ожидание" and looks stuck.
    private enum Display { Waiting, Downloading, Seeding, Error, Hashing }

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
    [ObservableProperty] private Brush _rowBackground = RowBrushes.Current.Idle;

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
            Display.Hashing     => "Проверка",
            _                   => "Ожидание",
        };
        var p = RowBrushes.Current;
        RowBackground = d switch
        {
            Display.Seeding     => IsSelected ? p.SeedingSelected     : p.Seeding,
            Display.Downloading => IsSelected ? p.DownloadingSelected : p.Downloading,
            Display.Error       => IsSelected ? p.ErrorSelected       : p.Error,
            // Hashing shares the Idle/Waiting tint — it's a passive-looking state that
            // the user just triggered; the text "Проверка" carries the meaning.
            _                   => IsSelected ? p.IdleSelected        : p.Idle,
        };
    }

    private static Display ComputeDisplay(TorrentManager m) => m.State switch
    {
        TorrentState.Seeding                                     => Display.Seeding,
        TorrentState.Error                                       => Display.Error,
        TorrentState.Hashing                                     => Display.Hashing,
        TorrentState.Downloading when m.Monitor.DownloadRate > 0 => Display.Downloading,
        _                                                        => Display.Waiting,
    };
}
