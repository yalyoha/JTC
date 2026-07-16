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
        SingleInstance.MagnetReceived += magnet =>
            window.DispatcherQueue.TryEnqueue(async () => await window.OpenMagnetAsync(magnet));
        SingleInstance.ShowWindowRequested += () =>
            window.DispatcherQueue.TryEnqueue(window.RestoreFromTray);
        // Installer signals shutdown via the inbox marker file so it can safely overwrite
        // JTC.exe and its DLLs. Hard-kill matches the tray "Выход" path — torrent state is
        // persisted after every add/remove/pause/resume, so nothing important is lost.
        SingleInstance.ShutdownRequested += () =>
        {
            DebugLog.Info("SingleInstance shutdown marker received — killing process");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        };
        SingleInstance.StartWatching();

        // Handle the launch argument this very process was given, if any — either a
        // .torrent file path (from Explorer / file association) or a magnet: URI (from
        // a browser via the URL scheme handler).
        var initial = SingleInstance.ExtractLaunchSource(cliArgs);
        if (initial is not null)
        {
            if (initial.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                _ = _mainWindow.OpenMagnetAsync(initial);
            else
                _ = _mainWindow.OpenTorrentPathAsync(initial);
        }
    }
}
