using Microsoft.UI.Xaml;
using TClient.Services;
using TClient.ViewModels;

namespace TClient;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly TorrentService _service;

    public MainWindow(TorrentService service)
    {
        _service = service;
        InitializeComponent();
        ViewModel = new MainViewModel(service, DispatcherQueue);
        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Stop();
        await _service.DisposeAsync();
    }
}
