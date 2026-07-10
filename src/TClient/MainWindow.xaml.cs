using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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

        // Translucent + blurred window (matches user spec: "полупрозрачное окно с размытием").
        SystemBackdrop = new DesktopAcrylicBackdrop();

        // Merge title bar into the client area so Acrylic reads through it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Reasonable initial size.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 640));
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Stop();
        await _service.DisposeAsync();
    }
}
