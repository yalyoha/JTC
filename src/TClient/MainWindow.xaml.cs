using System;
using System.Threading.Tasks;
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

    private async void OpenTorrentButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".torrent");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var folder = await PickDownloadFolderAsync();
        if (folder is null) return;

        try
        {
            await _service.AddTorrentFileAsync(file.Path, folder.Path, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not open this torrent", ex.Message);
        }
    }

    private async void AddMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        // Real magnet dialog comes in Task 16 — for now, no-op.
        await ShowErrorAsync("Not implemented yet", "Magnet input dialog is added in the next task.");
    }

    private async Task<Windows.Storage.StorageFolder?> PickDownloadFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFolderAsync();
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
