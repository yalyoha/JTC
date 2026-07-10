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
        var input = new Microsoft.UI.Xaml.Controls.TextBox
        {
            PlaceholderText = "magnet:?xt=urn:btih:...",
            AcceptsReturn = false,
        };
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Add magnet link",
            Content = input,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            return;

        var magnet = input.Text?.Trim();
        if (string.IsNullOrEmpty(magnet) || !magnet.StartsWith("magnet:"))
        {
            await ShowErrorAsync("Invalid magnet", "Text must start with \"magnet:\".");
            return;
        }

        var folder = await PickDownloadFolderAsync();
        if (folder is null) return;

        try
        {
            await _service.AddMagnetAsync(magnet, folder.Path, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not add magnet", ex.Message);
        }
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

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SelectedTorrent;
        if (vm is null) return;
        await _service.ResumeAsync(vm.Manager);
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SelectedTorrent;
        if (vm is null) return;
        await _service.PauseAsync(vm.Manager);
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SelectedTorrent;
        if (vm is null) return;

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Remove torrent",
            Content = $"Also delete downloaded files for “{vm.Name}”?",
            PrimaryButtonText = "Delete files",
            SecondaryButtonText = "Keep files",
            CloseButtonText = "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
            return; // Cancel

        var deleteFiles = result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
        await _service.RemoveAsync(vm.Manager, deleteFiles);
    }
}
