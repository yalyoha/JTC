using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using JTC.Helpers;
using JTC.Services;
using JTC.ViewModels;

namespace JTC;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly TorrentService _service;
    private bool _reallyExiting;

    public MainWindow(TorrentService service)
    {
        _service = service;
        InitializeComponent();
        ViewModel = new MainViewModel(service, DispatcherQueue);

        // Paint the window background + set element theme from the user's saved choice.
        // No OS backdrop is needed underneath — the theme brush covers the whole grid.
        var initialTheme = SettingsStore.Load().Theme;
        ThemeHelper.Apply(RootGrid, initialTheme);

        // Merge title bar into the client area so Acrylic reads through it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, initialTheme);

        // Reasonable initial size.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 640));

        // Window icon (taskbar + Alt-Tab). The .exe icon is set separately via <ApplicationIcon> in csproj.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tclient.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null
            ? ""
            : v.Revision > 0
                ? $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                : $"v{v.Major}.{v.Minor}.{v.Build}";

        // Torrent client stays alive when the user closes the window — engine keeps
        // seeding/leeching in the background. Real quit is only via the tray "Выход" item.
        AppWindow.Closing += OnAppWindowClosing;
        TrayIcon.LeftClickCommand = new RelayCommand(RestoreFromTray);

        // Wire tray context-flyout items. Use Command instead of Click event because
        // clicks on MenuFlyoutItems inside TaskbarIcon.ContextFlyout haven't been
        // firing in this H.NotifyIcon.WinUI version (verified via empty debug.log).
        TrayShowItem.Command = new RelayCommand(() =>
        {
            DebugLog.Info("Tray 'Показать' command fired");
            RestoreFromTray();
        });
        TrayExitItem.Command = new RelayCommand(() =>
        {
            DebugLog.Info("Tray 'Выход' command fired — killing process");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        });

        // Diagnostic: log whenever the flyout opens so we can tell if the XAML menu
        // is even showing, vs Windows drawing its own menu we can't intercept.
        if (TrayIcon.ContextFlyout is Microsoft.UI.Xaml.Controls.MenuFlyout mf)
        {
            mf.Opened += (_, _) => DebugLog.Info("Tray context flyout opened");
        }
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
                                    Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_reallyExiting) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    public void RestoreFromTray()
    {
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
        Activate();
    }

    private void TrayShow_Click(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        // Kill immediately, no cleanup, no await, nothing that could throw or block
        // before we reach TerminateProcess. Torrent state is persisted after every
        // add/remove/pause/resume, so a hard-kill loses nothing important. Log first
        // so we can confirm the handler even fires (previous versions with graceful
        // shutdown appeared to never reach the exit call at all).
        DebugLog.Info("TrayExit_Click fired — killing process");
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    private async void OpenTorrentButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".torrent");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await OpenTorrentPathAsync(file.Path);
    }

    /// <summary>
    /// Adds a torrent from a known file path (used by file-association activation from Windows
    /// Explorer). Reuses the same folder-picking logic as the manual button.
    /// </summary>
    public async Task OpenTorrentPathAsync(string torrentPath)
    {
        var downloadDir = await GetOrPickDownloadDirAsync();
        if (downloadDir is null) return;

        try
        {
            await _service.AddTorrentFileAsync(torrentPath, downloadDir, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось открыть торрент", ex.Message);
        }
    }

    /// <summary>
    /// Adds a magnet URI (used by URL-scheme activation from browsers — "magnet:..." link
    /// in a webpage). Reuses the same folder-picking logic as the manual button.
    /// </summary>
    public async Task OpenMagnetAsync(string magnetUri)
    {
        var downloadDir = await GetOrPickDownloadDirAsync();
        if (downloadDir is null) return;

        try
        {
            await _service.AddMagnetAsync(magnetUri, downloadDir, startImmediately: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось добавить magnet", ex.Message);
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
        ThemeHelper.ApplyToDialog(dialog, ThemeHelper.CurrentTheme);
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
        ThemeHelper.ApplyToDialog(dialog, ThemeHelper.CurrentTheme);
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Click on empty space inside the ListView (below the last row, header area, scrollbar
    /// margin) clears selection. WinUI's Extended selection mode has no built-in equivalent —
    /// once you click a row you can only deselect via Ctrl-click on that same row, which most
    /// users don't discover.
    /// </summary>
    private void TorrentList_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is Microsoft.UI.Xaml.Controls.ListViewItem)
                return; // tap landed on a row — let normal selection logic run
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        TorrentList.SelectedItems.Clear();
    }

    /// <summary>
    /// Multi-select support: update every VM's IsSelected flag so the row-background brush
    /// reflects the full selection set, not just the primary SelectedItem.
    /// </summary>
    private void TorrentList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        var selected = new HashSet<JTC.ViewModels.TorrentViewModel>(
            TorrentList.SelectedItems.Cast<JTC.ViewModels.TorrentViewModel>());
        foreach (var vm in ViewModel.Torrents)
            vm.IsSelected = selected.Contains(vm);
    }

    private List<JTC.ViewModels.TorrentViewModel> SelectedVMs() =>
        TorrentList.SelectedItems.Cast<JTC.ViewModels.TorrentViewModel>().ToList();

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        var vms = SelectedVMs();
        if (vms.Count == 0) return;
        foreach (var vm in vms)
        {
            try { await _service.ResumeAsync(vm.Manager); }
            catch (Exception ex) { await ShowErrorAsync("Не удалось возобновить", ex.Message); break; }
        }
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        var vms = SelectedVMs();
        if (vms.Count == 0) return;
        foreach (var vm in vms)
        {
            try { await _service.PauseAsync(vm.Manager); }
            catch (Exception ex) { await ShowErrorAsync("Не удалось приостановить", ex.Message); break; }
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var vms = SelectedVMs();
        if (vms.Count == 0)
        {
            await ShowErrorAsync("Ничего не выбрано", "Сначала выделите торрент в списке.");
            return;
        }
        await DeleteWithConfirmationAsync(vms);
    }

    /// <summary>
    /// Shared delete flow — used by the toolbar (multi-select) and the row context menu
    /// (single row). Shows the "delete files / keep files / cancel" dialog, then optimistically
    /// removes the rows from the UI and lets the service work through them serially.
    /// </summary>
    private async Task DeleteWithConfirmationAsync(List<TorrentViewModel> vms)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = vms.Count == 1 ? "Удалить торрент" : $"Удалить торренты ({vms.Count})",
            Content = vms.Count == 1
                ? $"Также удалить скачанные файлы «{vms[0].Name}»?"
                : $"Также удалить скачанные файлы для {vms.Count} выбранных торрентов?",
            PrimaryButtonText = "Удалить файлы",
            SecondaryButtonText = "Оставить файлы",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Secondary,
            XamlRoot = Content.XamlRoot,
        };
        ThemeHelper.ApplyToDialog(dialog, ThemeHelper.CurrentTheme);
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
            return; // Отмена

        var deleteFiles = result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;

        // Optimistic UI: pull every row out of the list right away, then let the service
        // work through them sequentially in the background (RemoveAsync is serialized by
        // TorrentService's internal semaphore).
        var toRemove = vms.Select(v => (vm: v, manager: v.Manager)).ToList();
        foreach (var (v, _) in toRemove)
        {
            v.Detach();
            ViewModel.Torrents.Remove(v);
        }
        ViewModel.SelectedTorrent = null;

        foreach (var (_, manager) in toRemove)
        {
            try { await _service.RemoveAsync(manager, deleteFiles); }
            catch { /* row already gone; engine failure is not user-actionable */ }
        }
    }

    /// <summary>
    /// Double-click a row → reveal the torrent's file or container folder in Explorer.
    /// </summary>
    private void TorrentRow_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TorrentViewModel vm)
            RevealInExplorer(vm.Manager);
    }

    private void RowOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TorrentViewModel vm)
            RevealInExplorer(vm.Manager);
    }

    private async void RowRecheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TorrentViewModel vm)
            return;
        try
        {
            await _service.RecheckAsync(vm.Manager);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Не удалось обновить", ex.Message);
        }
    }

    private async void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TorrentViewModel vm)
            return;
        await DeleteWithConfirmationAsync(new List<TorrentViewModel> { vm });
    }

    /// <summary>
    /// Opens the download folder in Windows Explorer with the torrent's file (single-
    /// file torrent) or its container folder (multi-file torrent) highlighted. Uses
    /// <c>explorer.exe /select,</c> so the user doesn't have to visually scan among
    /// unrelated files. Falls back to just opening the download folder when metadata
    /// hasn't arrived yet (magnet still resolving).
    /// </summary>
    private void RevealInExplorer(TorrentManager manager)
    {
        string? target = null;
        try
        {
            if (manager.Files is { Count: 1 } files && !string.IsNullOrEmpty(files[0].FullPath))
            {
                target = files[0].FullPath;
            }
            else if (manager.Torrent is not null && !string.IsNullOrEmpty(manager.Torrent.Name))
            {
                target = Path.Combine(manager.SavePath, manager.Torrent.Name);
            }
        }
        catch { /* fall through to fallback */ }

        if (string.IsNullOrEmpty(target))
        {
            if (Directory.Exists(manager.SavePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = manager.SavePath,
                        UseShellExecute = true,
                    });
                }
                catch { /* best-effort */ }
            }
            return;
        }

        // /select, opens Explorer at the parent folder with the given item selected.
        // Quote the path so spaces work; commas are impossible inside a Windows path.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target}\"",
                UseShellExecute = true,
            });
        }
        catch { /* best-effort */ }
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

        // Theme picker — indices match ItemsSource order below.
        var themeBox = new Microsoft.UI.Xaml.Controls.ComboBox
        {
            Header = "Тема",
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        themeBox.Items.Add("Фирменная");
        themeBox.Items.Add("Чёрная");
        themeBox.Items.Add("Белая");
        themeBox.SelectedIndex = current.Theme switch
        {
            AppTheme.Dark  => 1,
            AppTheme.Light => 2,
            _              => 0,
        };

        var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 16, MinWidth = 420 };
        panel.Children.Add(pathRow);
        panel.Children.Add(maxBox);
        panel.Children.Add(themeBox);

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Настройки",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        ThemeHelper.ApplyToDialog(dialog, ThemeHelper.CurrentTheme);
        var res = await dialog.ShowAsync();
        if (res != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;

        var newDir = pathBox.Text?.Trim();
        var newMax = (int)Math.Round(maxBox.Value);
        if (newMax < 1) newMax = 1;
        var newTheme = themeBox.SelectedIndex switch
        {
            1 => AppTheme.Dark,
            2 => AppTheme.Light,
            _ => AppTheme.Brand,
        };

        var updated = current with
        {
            LastDownloadDir = string.IsNullOrEmpty(newDir) ? null : newDir,
            MaxSimultaneousDownloads = newMax,
            Theme = newTheme,
        };
        SettingsStore.Save(updated);

        if (updated.Theme != current.Theme)
        {
            ThemeHelper.Apply(RootGrid, updated.Theme);
            ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, updated.Theme);
            // Cached row brushes belong to the old palette — RefreshBrushes forces every
            // VM to re-read RowBrushes.Current for its background, status stripe, and
            // foreground even when its Display state hasn't changed (ApplyDisplay's own
            // rebuild is state-change-gated, which would otherwise skip a pure theme swap).
            foreach (var vm in ViewModel.Torrents)
            {
                vm.RefreshBrushes();
                vm.Refresh();
            }
        }

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
