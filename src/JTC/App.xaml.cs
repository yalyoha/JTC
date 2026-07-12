using System;
using Microsoft.UI.Xaml;
using JTC.Services;

namespace JTC;

public partial class App : Application
{
    public static TorrentService? Service { get; private set; }

    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cliArgs = Environment.GetCommandLineArgs();

        // If another TClient instance is already running, hand off any .torrent arg to it
        // via the inbox file, then bail out. The primary picks it up.
        if (SingleInstance.TryClaimOrHandOff(cliArgs))
        {
            Exit();
            return;
        }

        Service = new TorrentService();
        _mainWindow = new MainWindow(Service);
        _mainWindow.Activate();

        // Idempotent — writes to HKCU only if a value actually changed.
        FileAssociation.EnsureRegistered();

        // Restore persisted torrents first, then let subsequent activations land afterwards.
        await Service.LoadStateAsync();

        // Route any inbox file-drops (from secondary "Open with" launches) into the UI.
        // An empty drop means "primary is in the tray, please show yourself" — this covers
        // the case where the user re-launches JTC while it's minimized.
        var window = _mainWindow;
        SingleInstance.TorrentPathReceived += path =>
            window.DispatcherQueue.TryEnqueue(async () => await window.OpenTorrentPathAsync(path));
        SingleInstance.ShowWindowRequested += () =>
            window.DispatcherQueue.TryEnqueue(window.RestoreFromTray);
        SingleInstance.StartWatching();

        // Handle the .torrent path this very process was launched with, if any.
        var initial = SingleInstance.ExtractTorrentPath(cliArgs);
        if (initial is not null)
            _ = _mainWindow.OpenTorrentPathAsync(initial);
    }
}
