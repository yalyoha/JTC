using System;
using System.IO;
using System.Reflection;
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

        // Mica BaseAlt: subtle desktop-wallpaper tint, distinctly less transparent than DesktopAcrylic.
        // Trade-off: no blur behind the window (that's an Acrylic-only effect), but readability is much better.
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };

        // Merge title bar into the client area so Acrylic reads through it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Reasonable initial size.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 640));

        // Window icon (taskbar + Alt-Tab). The .exe icon is set separately via <ApplicationIcon> in csproj.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tclient.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
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

        var downloadDir = await GetOrPickDownloadDirAsync();
        if (downloadDir is null) return;

        try
        {
            await _service.AddTorrentFileAsync(file.Path, downloadDir, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось открыть торрент", ex.Message);
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
            Title = "Добавить magnet-ссылку",
            Content = input,
            PrimaryButtonText = "Добавить",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            return;

        var magnet = input.Text?.Trim();
        if (string.IsNullOrEmpty(magnet) || !magnet.StartsWith("magnet:"))
        {
            await ShowErrorAsync("Неверная magnet-ссылка", "Текст должен начинаться с \"magnet:\".");
            return;
        }

        var downloadDir = await GetOrPickDownloadDirAsync();
        if (downloadDir is null) return;

        try
        {
            await _service.AddMagnetAsync(magnet, downloadDir, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось добавить magnet", ex.Message);
        }
    }

    private async Task<Windows.Storage.StorageFolder?> PickDownloadFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFolderAsync();
    }

    /// <summary>
    /// Returns the saved download directory if set and still exists on disk; otherwise prompts the user
    /// for a folder and saves that choice in settings.json for next time.
    /// </summary>
    private async Task<string?> GetOrPickDownloadDirAsync()
    {
        var settings = SettingsStore.Load();
        if (!string.IsNullOrEmpty(settings.LastDownloadDir) && Directory.Exists(settings.LastDownloadDir))
            return settings.LastDownloadDir;

        var folder = await PickDownloadFolderAsync();
        if (folder is null) return null;

        SettingsStore.Save(settings with { LastDownloadDir = folder.Path });
        return folder.Path;
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
        try
        {
            await _service.ResumeAsync(vm.Manager);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось возобновить", ex.Message);
        }
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SelectedTorrent;
        if (vm is null) return;
        try
        {
            await _service.PauseAsync(vm.Manager);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось приостановить", ex.Message);
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.SelectedTorrent;
        if (vm is null)
        {
            await ShowErrorAsync("Ничего не выбрано", "Сначала выделите торрент в списке.");
            return;
        }

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Удалить торрент",
            Content = $"Также удалить скачанные файлы «{vm.Name}»?",
            PrimaryButtonText = "Удалить файлы",
            SecondaryButtonText = "Оставить файлы",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
            return; // Отмена

        var deleteFiles = result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;

        // Optimistic UI: the row disappears immediately when the user confirms.
        // The engine cleanup happens in the background — any failure there doesn't matter
        // to the user; the torrent is gone from their point of view.
        var manager = vm.Manager;
        vm.Detach();
        ViewModel.Torrents.Remove(vm);
        if (ViewModel.SelectedTorrent == vm)
            ViewModel.SelectedTorrent = null;

        try
        {
            await _service.RemoveAsync(manager, deleteFiles);
        }
        catch
        {
            // Engine failed to unregister — not user-actionable. The row is already gone.
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var current = SettingsStore.Load();

        var pathBox = new Microsoft.UI.Xaml.Controls.TextBox
        {
            Header = "Папка загрузок",
            Text = current.LastDownloadDir ?? "",
            IsReadOnly = true,
            MinWidth = 340,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var browseBtn = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "Обзор…",
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 0),
        };
        browseBtn.Click += async (_, _) =>
        {
            var folder = await PickDownloadFolderAsync();
            if (folder is not null) pathBox.Text = folder.Path;
        };
        var pathRow = new Microsoft.UI.Xaml.Controls.Grid();
        pathRow.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(pathBox, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(browseBtn, 1);
        pathRow.Children.Add(pathBox);
        pathRow.Children.Add(browseBtn);

        var maxBox = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Header = "Одновременных закачек",
            Value = current.MaxSimultaneousDownloads,
            Minimum = 1,
            Maximum = 20,
            SmallChange = 1,
            LargeChange = 5,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Inline,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 16, MinWidth = 420 };
        panel.Children.Add(pathRow);
        panel.Children.Add(maxBox);

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Настройки",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var res = await dialog.ShowAsync();
        if (res != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;

        var newDir = pathBox.Text?.Trim();
        var newMax = (int)Math.Round(maxBox.Value);
        if (newMax < 1) newMax = 1;

        var updated = current with
        {
            LastDownloadDir = string.IsNullOrEmpty(newDir) ? null : newDir,
            MaxSimultaneousDownloads = newMax,
        };
        SettingsStore.Save(updated);

        try
        {
            await _service.ApplySettingsAsync(updated);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось применить настройки", ex.Message);
        }
    }
}
