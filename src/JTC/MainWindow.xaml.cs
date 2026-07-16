using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonoTorrent.Client;
using Windows.UI;
using JTC.Helpers;
using JTC.Services;
using JTC.ViewModels;

namespace JTC;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly TorrentService _service;

    // True while the window is hidden to the notification-area icon (X-close or explicit
    // Hide). Tracked explicitly instead of asking Win32 — AppWindow.Hide() clears
    // WS_VISIBLE, but that state can also be cleared by OS-level animation transitions,
    // and IsIconic vs IsWindowVisible give ambiguous answers when the window is animating
    // between visible/minimized/hidden. Our own flag has one owner (Hide / RestoreFromTray),
    // so there is no ambiguity.
    private bool _isHiddenToTray;

    public MainWindow(TorrentService service)
    {
        _service = service;
        InitializeComponent();
        ViewModel = new MainViewModel(service, DispatcherQueue);

        // Paint the window background + set element theme from the user's saved choice.
        // For the Colored theme, restore the last-saved gradient endpoints too (fall back
        // to first built-in preset if hex parsing fails or the fields are null).
        var initialSettings = SettingsStore.Load();
        var initialTheme = initialSettings.Theme;
        var initialTop = ThemeHelper.TryParseHex(initialSettings.ColoredTopHex);
        var initialBottom = ThemeHelper.TryParseHex(initialSettings.ColoredBottomHex);
        ThemeHelper.Apply(RootGrid, initialTheme, initialTop, initialBottom);

        // Merge title bar into the client area so Acrylic reads through it.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, initialTheme);

        // Reasonable initial size.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 640));

        // Prevent the user from shrinking the window past the point where the toolbar
        // buttons ("Добавить magnet") and header labels ("Прогресс") get squeezed into
        // a vertical character-per-line stack — see screenshots/img_22.png.
        InstallMinSizeSubclass();

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
        TrayIcon.LeftClickCommand = new RelayCommand(ToggleFromTray);

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
        // Real quit is only via the tray "Выход" (Process.Kill) — X-close always hides
        // to tray. No _reallyExiting bypass: we don't want an accidental Close() call to
        // shut the engine down mid-download.
        args.Cancel = true;
        AppWindow.Hide();
        _isHiddenToTray = true;
        DebugLog.Info("Window closing → hidden to tray");
    }

    public void RestoreFromTray()
    {
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
        Activate();
        _isHiddenToTray = false;
        DebugLog.Info("Window restored from tray");
    }

    /// <summary>
    /// Tray left-click toggle: hide the window if it's currently shown, restore it if
    /// hidden. Uses our own tracked flag (see _isHiddenToTray comment) rather than
    /// Win32 IsWindowVisible so the state is unambiguous during animation transitions.
    /// A minimized window (user pressed the minimize caption button) counts as "shown"
    /// and click will hide it — matches how Discord / Telegram tray icons behave.
    /// </summary>
    public void ToggleFromTray()
    {
        DebugLog.Info($"Tray left-click fired; hidden={_isHiddenToTray}");
        if (_isHiddenToTray)
        {
            RestoreFromTray();
        }
        else
        {
            AppWindow.Hide();
            _isHiddenToTray = true;
            DebugLog.Info("  → hidden to tray");
        }
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

        // For multi-file torrents (typical case: a TV season / album), let the user pick
        // which files to actually download. Parse the .torrent locally first — this doesn't
        // engage the engine and won't create a manager, so a Cancel here leaves no state
        // behind. Single-file torrents skip the dialog entirely (nothing to choose).
        IReadOnlySet<int>? skipIndices = null;
        try
        {
            var parsed = await MonoTorrent.Torrent.LoadAsync(torrentPath);
            if (parsed.Files.Count > 1)
            {
                var entries = parsed.Files
                    .Select((f, i) => new FileSelectionDialog.Entry(i, f.Path, f.Length))
                    .ToList();
                var selection = await FileSelectionDialog.ShowAsync(Content.XamlRoot, parsed.Name, entries);
                if (selection is null)
                    return; // user cancelled
                skipIndices = selection;
            }
        }
        catch (Exception ex)
        {
            // Parse failure is an add failure too — surface the same error.
            await ShowErrorAsync("Не удалось прочитать .torrent", ex.Message);
            return;
        }

        try
        {
            await _service.AddTorrentFileAsync(torrentPath, downloadDir, startImmediately: true, skipIndices);
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

    // --- Minimum window size (WM_GETMINMAXINFO subclass) ---------------------------
    // WinUI 3 / WindowsAppSDK 2.2 has no AppWindow.MinSize; enforce it via a Comctl32
    // subclass that clamps ptMinTrackSize on every WM_GETMINMAXINFO. Values are in
    // DIPs and scaled by the window's current DPI so the limit tracks per-monitor DPI
    // changes automatically (WM_GETMINMAXINFO fires again after WM_DPICHANGED).
    private const int MinWindowWidthDip  = 780;
    private const int MinWindowHeightDip = 360;

    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("Comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("User32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // Held as a field so the GC doesn't collect the delegate while native code still
    // holds the function pointer via SetWindowSubclass.
    private SUBCLASSPROC? _subclassProc;

    private void InstallMinSizeSubclass()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclassProc = MinSizeWndProc;
        SetWindowSubclass(hwnd, _subclassProc, 0, IntPtr.Zero);
    }

    private IntPtr MinSizeWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO)
        {
            var scale = GetDpiForWindow(hWnd) / 96.0;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.x = (int)Math.Ceiling(MinWindowWidthDip  * scale);
            mmi.ptMinTrackSize.y = (int)Math.Ceiling(MinWindowHeightDip * scale);
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var current = SettingsStore.Load();

        // Snapshot the live state so we can revert on Cancel — the color-picker flyouts
        // apply live-preview to the window and title bar as the user drags, so we need
        // a way back to "exactly what was on screen before the dialog opened".
        var snapshotTheme  = ThemeHelper.CurrentTheme;
        var snapshotTop    = ThemeHelper.CurrentTop;
        var snapshotBottom = ThemeHelper.CurrentBottom;

        // Forward-declared so ApplyLiveTheme can close over the dialog reference — the
        // dialog itself is constructed further down (needs all the child controls first).
        Microsoft.UI.Xaml.Controls.ContentDialog? dialog = null;

        // Current working colors — start from settings, fall back to the first built-in
        // preset if the stored hex is unparseable or missing (fresh install).
        var workingTop = ThemeHelper.TryParseHex(current.ColoredTopHex)
                         ?? ThemeHelper.TryParseHex(BuiltInColorPresets.PinkTopHex)!.Value;
        var workingBottom = ThemeHelper.TryParseHex(current.ColoredBottomHex)
                            ?? ThemeHelper.TryParseHex(BuiltInColorPresets.PinkBottomHex)!.Value;

        // ---- LEFT column: existing settings ---------------------------------------
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

        // Theme picker — indices match themeItems below.
        var themeItems = new[]
        {
            (Label: "Цветная", Theme: AppTheme.Colored),
            (Label: "Чёрная",  Theme: AppTheme.Dark),
            (Label: "Белая",   Theme: AppTheme.Light),
        };
        var themeBox = new Microsoft.UI.Xaml.Controls.ComboBox
        {
            Header = "Тема",
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var t in themeItems) themeBox.Items.Add(t.Label);
        themeBox.SelectedIndex = System.Array.FindIndex(themeItems, t => t.Theme == current.Theme);
        if (themeBox.SelectedIndex < 0) themeBox.SelectedIndex = 0;

        // Path row is promoted to the full-width top of the dialog (see outerPanel below);
        // the left column keeps just the two narrow controls under it.
        var leftCol = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 16, MinWidth = 260 };
        leftCol.Children.Add(maxBox);
        leftCol.Children.Add(themeBox);

        // ---- RIGHT column: color pickers + presets (only when Colored) ------------
        // Working copy of the preset list so Save-as-preset / Delete stay local until
        // the user hits Сохранить at the dialog level.
        var workingPresets = new List<ColorPreset>(current.CustomPresets);

        var presetBox = new Microsoft.UI.Xaml.Controls.ComboBox
        {
            Header = "Пресет",
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        void RebuildPresetItems(int? selectIndex = null)
        {
            var selectedName = presetBox.SelectedItem as string;
            presetBox.Items.Clear();
            foreach (var p in BuiltInColorPresets.All) presetBox.Items.Add(p.Name);
            foreach (var p in workingPresets)          presetBox.Items.Add(p.Name);
            if (selectIndex is int idx && idx >= 0 && idx < presetBox.Items.Count)
                presetBox.SelectedIndex = idx;
            else if (selectedName is not null)
            {
                for (int i = 0; i < presetBox.Items.Count; i++)
                    if ((string)presetBox.Items[i] == selectedName) { presetBox.SelectedIndex = i; break; }
            }
        }

        ColorPreset? PresetAt(int i)
        {
            if (i < 0) return null;
            if (i < BuiltInColorPresets.All.Count) return BuiltInColorPresets.All[i];
            var j = i - BuiltInColorPresets.All.Count;
            return (j >= 0 && j < workingPresets.Count) ? workingPresets[j] : null;
        }

        bool IsBuiltInIndex(int i) => i >= 0 && i < BuiltInColorPresets.All.Count;

        int FindPresetIndexMatchingColors(Color top, Color bottom)
        {
            var topHex    = ThemeHelper.ToHex(top);
            var bottomHex = ThemeHelper.ToHex(bottom);
            for (int i = 0; i < BuiltInColorPresets.All.Count; i++)
            {
                var p = BuiltInColorPresets.All[i];
                if (string.Equals(p.TopHex, topHex, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.BottomHex, bottomHex, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            for (int j = 0; j < workingPresets.Count; j++)
            {
                var p = workingPresets[j];
                if (string.Equals(p.TopHex, topHex, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.BottomHex, bottomHex, System.StringComparison.OrdinalIgnoreCase))
                    return BuiltInColorPresets.All.Count + j;
            }
            return -1;
        }

        // Color swatches — small colored rectangles that pop a WinUI ColorPicker on click.
        // Kept as Border rather than Button because Button carries the default WinUI hover
        // overlay (a translucent grey lift) which reads as visual noise on a pure-colour
        // sample — the swatch should look identical whether the pointer is over it or not.
        Border MakeSwatchBorder(Color initial)
        {
            return new Border
            {
                Width = 48,
                Height = 28,
                Background = new SolidColorBrush(initial),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            };
        }

        var topSwatch    = MakeSwatchBorder(workingTop);
        var bottomSwatch = MakeSwatchBorder(workingBottom);

        Microsoft.UI.Xaml.Controls.Flyout MakeColorFlyout(Color initial, System.Action<Color> onChanged)
        {
            var picker = new Microsoft.UI.Xaml.Controls.ColorPicker
            {
                Color = initial,
                IsAlphaEnabled = false,
                IsAlphaSliderVisible = false,
                IsAlphaTextInputVisible = false,
                IsHexInputVisible = true,
                IsColorChannelTextInputVisible = false,
                IsMoreButtonVisible = false,
                IsColorPreviewVisible = true,
                IsColorSliderVisible = true,
                ColorSpectrumShape = Microsoft.UI.Xaml.Controls.ColorSpectrumShape.Box,
            };
            picker.ColorChanged += (_, args) => onChanged(args.NewColor);
            return new Microsoft.UI.Xaml.Controls.Flyout
            {
                Content = picker,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
            };
        }

        void ApplyLiveTheme()
        {
            var themeIdx = System.Math.Max(0, themeBox.SelectedIndex);
            var theme = themeItems[themeIdx].Theme;
            ThemeHelper.Apply(RootGrid, theme, workingTop, workingBottom);
            ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, theme);
            foreach (var vm in ViewModel.Torrents)
            {
                vm.RefreshBrushes();
                vm.Refresh();
            }
            // Keep the open settings dialog in sync so its surface + Save button don't
            // stay frozen at the colours the dialog was originally shown with.
            if (dialog is not null && theme == AppTheme.Colored)
                ThemeHelper.RepaintColoredDialog(dialog, workingTop, workingBottom);
        }

        var topFlyout = MakeColorFlyout(workingTop, c =>
        {
            workingTop = c;
            topSwatch.Background = new SolidColorBrush(c);
            // Manual color pick means the preset selection may no longer match.
            var match = FindPresetIndexMatchingColors(workingTop, workingBottom);
            presetBox.SelectedIndex = match;
            ApplyLiveTheme();
        });
        FlyoutBase.SetAttachedFlyout(topSwatch, topFlyout);
        topSwatch.Tapped += (_, _) => FlyoutBase.ShowAttachedFlyout(topSwatch);

        var bottomFlyout = MakeColorFlyout(workingBottom, c =>
        {
            workingBottom = c;
            bottomSwatch.Background = new SolidColorBrush(c);
            var match = FindPresetIndexMatchingColors(workingTop, workingBottom);
            presetBox.SelectedIndex = match;
            ApplyLiveTheme();
        });
        FlyoutBase.SetAttachedFlyout(bottomSwatch, bottomFlyout);
        bottomSwatch.Tapped += (_, _) => FlyoutBase.ShowAttachedFlyout(bottomSwatch);

        // Rows for the two swatches — [Label] [Swatch]. Kept as narrow grids so both
        // rows align visually.
        Microsoft.UI.Xaml.Controls.Grid MakeSwatchRow(string label, FrameworkElement swatch)
        {
            var g = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(tb, 0);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(swatch, 1);
            g.Children.Add(tb);
            g.Children.Add(swatch);
            return g;
        }

        var savePresetBtn = new Microsoft.UI.Xaml.Controls.Button { Content = "Сохранить пресет…" };
        var deletePresetBtn = new Microsoft.UI.Xaml.Controls.Button { Content = "Удалить пресет" };
        var presetActionsRow = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        presetActionsRow.Children.Add(savePresetBtn);
        presetActionsRow.Children.Add(deletePresetBtn);

        var rightCol = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 12,
            MinWidth = 240,
            Margin = new Thickness(24, 0, 0, 0),
        };
        rightCol.Children.Add(presetBox);
        rightCol.Children.Add(MakeSwatchRow("Верхний цвет", topSwatch));
        rightCol.Children.Add(MakeSwatchRow("Нижний цвет",  bottomSwatch));
        rightCol.Children.Add(presetActionsRow);

        // Initial preset selection: try to match current colors to a preset.
        RebuildPresetItems();
        presetBox.SelectedIndex = FindPresetIndexMatchingColors(workingTop, workingBottom);
        deletePresetBtn.IsEnabled = presetBox.SelectedIndex >= 0 && !IsBuiltInIndex(presetBox.SelectedIndex);

        presetBox.SelectionChanged += (_, _) =>
        {
            var p = PresetAt(presetBox.SelectedIndex);
            if (p is not null)
            {
                var top    = ThemeHelper.TryParseHex(p.TopHex);
                var bottom = ThemeHelper.TryParseHex(p.BottomHex);
                if (top is not null && bottom is not null)
                {
                    workingTop = top.Value;
                    workingBottom = bottom.Value;
                    topSwatch.Background = new SolidColorBrush(workingTop);
                    bottomSwatch.Background = new SolidColorBrush(workingBottom);
                    ApplyLiveTheme();
                }
            }
            deletePresetBtn.IsEnabled = presetBox.SelectedIndex >= 0 && !IsBuiltInIndex(presetBox.SelectedIndex);
        };

        // Right column is only relevant for Colored — hide entirely for Dark / Light and
        // toggle live-preview to the new theme every time the picker changes.
        void UpdateRightColumnVisibility()
        {
            var themeIdx = System.Math.Max(0, themeBox.SelectedIndex);
            rightCol.Visibility = themeItems[themeIdx].Theme == AppTheme.Colored
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        UpdateRightColumnVisibility();
        themeBox.SelectionChanged += (_, _) =>
        {
            UpdateRightColumnVisibility();
            ApplyLiveTheme();
        };

        // Bottom-half two-column grid: [maxBox+themeBox] | [presets + swatches].
        // Auto column widths so each column takes exactly as much space as it needs; the
        // outer StackPanel then centres the whole strip under the full-width path row.
        var bottomGrid = new Microsoft.UI.Xaml.Controls.Grid();
        bottomGrid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(leftCol, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(rightCol, 1);
        bottomGrid.Children.Add(leftCol);
        bottomGrid.Children.Add(rightCol);

        // Outer StackPanel: path row on top (full width thanks to StackPanel's default
        // horizontal-stretch), everything else stacked below in the two-column bottomGrid.
        var outerPanel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 16 };
        outerPanel.Children.Add(pathRow);
        outerPanel.Children.Add(bottomGrid);

        dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Настройки",
            Content = outerPanel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        // ContentDialog's default MaxWidth (theme resource, ~548 dip) is too narrow for the
        // two-column layout — the right column with presets/swatches gets clipped. Push it up
        // to 900 dip so both columns fit comfortably; the dialog auto-shrinks if the content
        // is smaller (Dark/Light theme collapses the right column).
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;
        ThemeHelper.ApplyToDialog(dialog, ThemeHelper.CurrentTheme);

        // Save-as-preset: nested ContentDialog for the name, then append and select it.
        savePresetBtn.Click += async (_, _) =>
        {
            var nameBox = new Microsoft.UI.Xaml.Controls.TextBox
            {
                Header = "Название пресета",
                PlaceholderText = "например, Закат",
                MinWidth = 260,
            };
            var nameDialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "Сохранить пресет",
                Content = nameBox,
                PrimaryButtonText = "Сохранить",
                CloseButtonText = "Отмена",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            ThemeHelper.ApplyToDialog(nameDialog, ThemeHelper.CurrentTheme);
            var nameResult = await nameDialog.ShowAsync();
            if (nameResult != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;

            var name = nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Reject duplicate names (case-insensitive) so the ComboBox doesn't grow two
            // entries with the same label — user has to pick a different name or delete
            // the existing one first.
            bool duplicate = BuiltInColorPresets.All.Any(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                          || workingPresets.Any(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                await ShowErrorAsync("Пресет уже существует", $"Пресет с именем «{name}» уже есть. Выберите другое имя.");
                return;
            }

            workingPresets.Add(new ColorPreset
            {
                Name = name,
                TopHex = ThemeHelper.ToHex(workingTop),
                BottomHex = ThemeHelper.ToHex(workingBottom),
            });
            RebuildPresetItems(BuiltInColorPresets.All.Count + workingPresets.Count - 1);
        };

        deletePresetBtn.Click += (_, _) =>
        {
            var i = presetBox.SelectedIndex;
            if (i < 0 || IsBuiltInIndex(i)) return;
            var j = i - BuiltInColorPresets.All.Count;
            workingPresets.RemoveAt(j);
            RebuildPresetItems();
            presetBox.SelectedIndex = FindPresetIndexMatchingColors(workingTop, workingBottom);
        };

        var res = await dialog.ShowAsync();
        if (res != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            // Revert live-preview changes: restore snapshot theme + colors.
            ThemeHelper.Apply(RootGrid, snapshotTheme, snapshotTop, snapshotBottom);
            ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, snapshotTheme);
            foreach (var vm in ViewModel.Torrents)
            {
                vm.RefreshBrushes();
                vm.Refresh();
            }
            return;
        }

        var newDir = pathBox.Text?.Trim();
        var newMax = (int)Math.Round(maxBox.Value);
        if (newMax < 1) newMax = 1;
        var newTheme = themeItems[System.Math.Max(0, themeBox.SelectedIndex)].Theme;

        var updated = current with
        {
            LastDownloadDir = string.IsNullOrEmpty(newDir) ? null : newDir,
            MaxSimultaneousDownloads = newMax,
            Theme = newTheme,
            ColoredTopHex    = ThemeHelper.ToHex(workingTop),
            ColoredBottomHex = ThemeHelper.ToHex(workingBottom),
            CustomPresets    = workingPresets,
        };
        SettingsStore.Save(updated);

        // Ensure the window matches saved state — live preview already applied it while
        // the dialog was open, but re-apply here so a "Save without visiting any picker"
        // path still commits correctly (theme changed via combobox, colors unchanged).
        ThemeHelper.Apply(RootGrid, updated.Theme, workingTop, workingBottom);
        ThemeHelper.ApplyToTitleBar(AppWindow.TitleBar, updated.Theme);
        foreach (var vm in ViewModel.Torrents)
        {
            vm.RefreshBrushes();
            vm.Refresh();
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
