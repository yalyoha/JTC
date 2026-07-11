using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using MonoTorrent.Client;
using TClient.Helpers;

namespace TClient.ViewModels;

public sealed partial class TorrentViewModel : ObservableObject
{
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

    public TorrentViewModel(TorrentManager manager, DispatcherQueue dispatcher)
    {
        Manager = manager;
        _dispatcher = dispatcher;

        Name = manager.Torrent?.Name ?? "(загрузка метаданных…)";
        SizeText = manager.Torrent is null ? "—" : Formatting.BytesToHuman(manager.Torrent.Size);
        StateText = Formatting.StateToRu(manager.State);

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
        StateText = Formatting.StateToRu(Manager.State);
        IsPaused = Manager.State is TorrentState.Paused or TorrentState.Stopped;
    }

    private void OnStateChanged(object? sender, TorrentStateChangedEventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            StateText = Formatting.StateToRu(e.NewState);
            IsPaused = e.NewState is TorrentState.Paused or TorrentState.Stopped;
        });
    }

    public void Detach()
    {
        Manager.TorrentStateChanged -= OnStateChanged;
    }
}
