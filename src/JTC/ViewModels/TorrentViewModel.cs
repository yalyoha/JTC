using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using Windows.UI;
using JTC.Helpers;

namespace JTC.ViewModels;

public sealed partial class TorrentViewModel : ObservableObject
{
    // Row rendering has three moving parts:
    //   RowBackground — LinearGradientBrush painting the plashka. Fill (left) → Bg (right)
    //                   with a hard seam at Progress %. Bg and Fill come from the theme.
    //   StatusBrush   — 4 px left-edge stripe. Neon Material A400 hue per state; identical
    //                   across themes so the state reads at a glance on white or dark.
    //   RowForeground — Text color for every TextBlock in the row. Theme-scoped (dark on
    //                   light plashka, white on dark plashka).

    // Simplified user-visible state. Everything the engine reports (Paused, Stopped,
    // Starting, Metadata, Downloading-with-zero-rate, …) collapses to Waiting; Downloading
    // with a live rate is the only "actively pulling bytes" state. Hashing is broken out
    // separately so the user can see rechecks in progress after "Обновить" — otherwise a
    // multi-minute hash check just shows as "Ожидание" and looks stuck.
    private enum Display { Waiting, Downloading, Seeding, Error, Hashing }

    private readonly DispatcherQueue _dispatcher;
    private Display _current = Display.Waiting;

    public TorrentManager Manager { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private double _progress;         // 0..100
    [ObservableProperty] private string _progressText = "0.0%";
    [ObservableProperty] private string _downloadRateText = "—";
    [ObservableProperty] private string _uploadRateText = "—";
    [ObservableProperty] private int _peerCount;
    [ObservableProperty] private string _stateText = "Ожидание";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Brush _rowBackground = new SolidColorBrush();
    [ObservableProperty] private Brush _statusBrush = new SolidColorBrush();
    [ObservableProperty] private Brush _rowForeground = new SolidColorBrush();
    // Bound to the row's Border.CornerRadius via x:Bind so a settings-dialog change
    // takes effect on every row without walking the visual tree. Default from
    // ThemeHelper's current cached value so freshly-added torrents inherit whatever
    // the user last picked.
    [ObservableProperty] private Microsoft.UI.Xaml.CornerRadius _plashkaCornerRadius =
        new Microsoft.UI.Xaml.CornerRadius(ThemeHelper.CurrentPlashkaCornerRadius);

    // Toggles between the two status-indicator shapes on each row: Circle = 8×8 dot,
    // Stripe = 4 px left-edge bar. XAML uses two elements with mutually-exclusive
    // Visibility bindings, so only one is ever laid out at a time.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCircleVisibility))]
    [NotifyPropertyChangedFor(nameof(StatusStripeVisibility))]
    private bool _statusAsCircle = ThemeHelper.CurrentStatusIndicatorStyle == JTC.Services.StatusIndicatorStyle.Circle;

    public Microsoft.UI.Xaml.Visibility StatusCircleVisibility =>
        StatusAsCircle ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility StatusStripeVisibility =>
        StatusAsCircle ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    partial void OnIsSelectedChanged(bool value) => RebuildRowBackground();
    partial void OnProgressChanged(double value) => RebuildRowBackground();

    public TorrentViewModel(TorrentManager manager, DispatcherQueue dispatcher)
    {
        Manager = manager;
        _dispatcher = dispatcher;

        Name = manager.Torrent?.Name ?? "(загрузка метаданных…)";
        SizeText = manager.Torrent is null ? "—" : Formatting.BytesToHuman(manager.Torrent.Size);
        ApplyDisplay(ComputeDisplay(manager));
        RebuildRowBackground();

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

        // Set Progress before ApplyDisplay so OnProgressChanged fires against the
        // fresh Progress value; when the state also changes, ApplyDisplay's own
        // RebuildRowBackground picks up the new state with the already-fresh progress.
        Progress = Manager.Progress;
        ProgressText = $"{Manager.Progress:F1}%";
        DownloadRateText = Formatting.RateToHuman(Manager.Monitor.DownloadRate);
        UploadRateText = Formatting.RateToHuman(Manager.Monitor.UploadRate);
        PeerCount = Manager.Peers.Available;
        IsPaused = Manager.State is TorrentState.Paused or TorrentState.Stopped;
        ApplyDisplay(ComputeDisplay(Manager));
    }

    // Force-rebuild all three brushes without waiting for a state / progress / selection
    // event. Called after a theme switch — RowBrushes.Current has changed but nothing on
    // the VM itself did, so none of the OnXxxChanged partials would fire.
    public void RefreshBrushes() => RebuildRowBackground();

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
        if (_current == d)
            return;
        _current = d;
        StateText = d switch
        {
            Display.Seeding     => "Раздача",
            Display.Downloading => "Загрузка",
            Display.Error       => "Ошибка",
            Display.Hashing     => "Проверка",
            _                   => "Ожидание",
        };
        RebuildRowBackground();
    }

    private void RebuildRowBackground()
    {
        var p = RowBrushes.Current;
        var bg = IsSelected ? p.BgSelected : p.Bg;

        // The row's "progress bar" fill (left band of the gradient, growing with
        // Progress %) is now a 17 %-opacity composite of the row's current status
        // colour on top of the plashka bg. So a downloading row gets a soft orange
        // tint under its progress %, a hashing row gets a soft blue tint, etc.
        // Both the 4 px left-edge stripe (full opacity) and this tint react to the
        // same status colour — user-picked from settings, RowBrushes.StatusXxx.
        var statusColor = _current switch
        {
            Display.Downloading => RowBrushes.StatusDownloading,
            Display.Seeding     => RowBrushes.StatusSeeding,
            Display.Hashing     => RowBrushes.StatusHashing,
            Display.Error       => RowBrushes.StatusError,
            _                   => RowBrushes.StatusIdle,
        };
        // Seeding = 100 %. If we tinted the whole row it would look "done but still
        // active"; the finished-torrent mockup calls for a plain plashka, so force
        // fill = bg for Seeding. Otherwise 25 % of the status colour composited over
        // the plashka bg — bumped from 17 % in v0.5.5 because on user-picked pastel
        // status colours over a matching-tone plashka the fill was nearly indistinguishable
        // from the background after switching between certain theme presets, and users
        // read that as "progress bar disappeared".
        var fill = _current == Display.Seeding
            ? bg
            : RowBrushes.CompositeOver(bg, statusColor, 0.25);

        RowBackground = BuildRowBrush(bg, fill, Progress);
        StatusBrush   = new SolidColorBrush(statusColor);
        RowForeground = new SolidColorBrush(p.Fg);
    }

    // Four gradient stops with two pairs sharing an offset produce a hard vertical
    // seam at Progress% — visually two solid bands, not a gradient. Clamp keeps the
    // seam inside [0,1] even when Progress briefly reads outside 0..100.
    private static LinearGradientBrush BuildRowBrush(Color bg, Color fill, double progress)
    {
        var p = Math.Clamp(progress / 100.0, 0.0, 1.0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint   = new Windows.Foundation.Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop { Color = fill, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = fill, Offset = p });
        brush.GradientStops.Add(new GradientStop { Color = bg,   Offset = p });
        brush.GradientStops.Add(new GradientStop { Color = bg,   Offset = 1 });
        return brush;
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
