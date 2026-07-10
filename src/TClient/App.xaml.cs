using Microsoft.UI.Xaml;
using TClient.Services;

namespace TClient;

public partial class App : Application
{
    public static TorrentService? Service { get; private set; }
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Service = new TorrentService();
        _window = new MainWindow(Service);
        _window.Activate();
        await Service.LoadStateAsync();
    }
}
