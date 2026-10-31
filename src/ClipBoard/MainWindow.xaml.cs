using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipBoard.Core;
using Forms = System.Windows.Forms;

namespace ClipBoard;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        private set => SetValue(IsCompactProperty, value);
    }

    private readonly ClipboardHistory history = new();
    private readonly EncryptedHistoryStore store;
    private readonly ShortcutSettingsStore shortcutStore;
    private ShortcutSettings shortcutSettings = new();
    private GlobalShortcutService? shortcuts;
    private ShortcutSettingsWindow? shortcutDialog;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer retentionTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private NativeClipboardMonitor? monitor;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? trayPause;
    private Forms.ToolStripMenuItem? trayOverride;
    private Forms.ToolStripMenuItem? trayShortcut;
    private System.Drawing.Icon? trayIcon;
    private Task pendingSave = Task.CompletedTask;
    private bool paused;
    private bool exiting;
    private bool exitRequested;
    private bool persistenceBlocked;
    private bool initialized;
    private PasteTarget? pasteTarget;
    private Guid? pressedClip;
    private bool openingWindow;
    private bool pasting;

    internal bool HasPasteTarget => pasteTarget is not null;

    public MainWindow(EncryptedHistoryStore? store = null, bool startPaused = false)
    {
        this.store = store ?? new EncryptedHistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBoard", "history.dat"));
        shortcutStore = new ShortcutSettingsStore(Path.Combine(Path.GetDirectoryName(this.store.FilePath)!, "settings.json"));
        paused = startPaused;
        InitializeComponent();
        initialized = true;
        try { history.Restore(this.store.Load()); }
        catch (Exception exception) when (IsStorageError(exception))
        {
            persistenceBlocked = true;
            SetStatus("History could not be opened. Existing file preserved; saving is disabled.");
        }
        saveTimer.Tick += async (_, _) => await SaveAsync();
        retentionTimer.Tick += (_, _) =>
        {
            if (history.Prune())
            {
                RefreshList();
                ScheduleSave();
            }
        };
        retentionTimer.Start();
        RefreshList();
        UpdatePauseState();
        try { shortcutSettings = shortcutStore.Load(); }
        catch (Exception exception) when (IsStorageError(exception))
        {
            shortcutSettings = new ShortcutSettings { OverrideWinV = false };
            SetStatus("Shortcut settings could not be opened. Using Alt+B; Win+V override is off.");
        }
        Loaded += (_, _) =>
        {
            NativeWindowPlacement.MoveToCursor(this);
            SearchBox.Focus();
        };
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        var handle = new WindowInteropHelper(this).Handle;
        try
        {
            monitor = new NativeClipboardMonitor(handle, CaptureText, ShowFromTray, SetStatus, paused);
        }
        catch (Win32Exception)
        {
            paused = true;
            UpdatePauseState();
            SetStatus("Windows could not start clipboard monitoring. Restart ClipBoard to retry.");
        }
        shortcuts = new GlobalShortcutService(handle, ShowFromTray);
        if (!shortcuts.TryConfigure(shortcutSettings, () => { }, out var shortcutError))
            SetStatus(shortcutError + " Open ClipBoard from the tray.");
        InitializeTray();
        UpdateSelection();
    }

    private void InitializeTray()
    {
        trayIcon = CreateTrayIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open ClipBoard", null, (_, _) => ShowFromTray());
        trayPause = new Forms.ToolStripMenuItem(paused ? "Resume capture" : "Pause capture");
        trayPause.Click += (_, _) => TogglePause();
        trayPause.Enabled = monitor is not null;
        menu.Items.Add(trayPause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        trayOverride = new Forms.ToolStripMenuItem("Override Win+V");
        trayOverride.Click += (_, _) =>
        {
            if (!TryApplyShortcutSettings(shortcutSettings with { OverrideWinV = !shortcuts!.WinVEnabled }, out var error))
                MessageBox.Show(error, "Shortcut unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        menu.Items.Add(trayOverride);
        trayShortcut = new Forms.ToolStripMenuItem();
        trayShortcut.Click += (_, _) => Dispatcher.BeginInvoke(new Action(OpenShortcutSettings));
        menu.Items.Add(trayShortcut);
        UpdateShortcutMenu();
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = paused ? "ClipBoard - capture paused" : "ClipBoard", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowFromTray();
    }

    internal bool TryApplyShortcutSettings(ShortcutSettings requested, out string error)
    {
        error = "Shortcut service is not ready.";
        if (shortcuts is null || !shortcuts.TryConfigure(requested, () => shortcutStore.Save(requested), out error))
            return false;
        shortcutSettings = requested;
        UpdateShortcutMenu();
        SetStatus(requested.OverrideWinV ? $"Win+V override on; {requested.Shortcut.Label} available"
            : $"Shortcut: {requested.Shortcut.Label}");
        return true;
    }

    private void UpdateShortcutMenu()
    {
        if (trayOverride is not null)
            trayOverride.Checked = shortcuts?.WinVEnabled == true;
        if (trayShortcut is not null)
            trayShortcut.Text = $"Keyboard shortcut ({shortcutSettings.Shortcut.Label})...";
    }

    private async void OpenShortcutSettings()
    {
        if (openingWindow || pasting || exitRequested)
            return;
        if (shortcutDialog is not null)
        {
            shortcutDialog.Activate();
            return;
        }
        await ShowFromTrayAsync();
        shortcutDialog = new ShortcutSettingsWindow(shortcutSettings,
            requested => TryApplyShortcutSettings(requested, out var error) ? null : error)
            { Owner = this, Resources = Resources };
        try { shortcutDialog.ShowDialog(); }
        finally { shortcutDialog = null; }
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var background = new SolidBrush(System.Drawing.Color.FromArgb(8, 126, 120));
        using var foreground = new System.Drawing.Pen(System.Drawing.Color.White, 2);
        graphics.FillRectangle(background, 2, 2, 28, 28);
        graphics.DrawRectangle(foreground, 9, 8, 14, 18);
        graphics.FillRectangle(System.Drawing.Brushes.White, 12, 5, 8, 5);
        graphics.DrawLine(foreground, 12, 15, 20, 15);
        graphics.DrawLine(foreground, 12, 20, 18, 20);
        var handle = bitmap.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private void CaptureText(string text)
    {
        if (history.Add(text))
        {
            RefreshList();
            ScheduleSave();
        }
    }

    private void RefreshList(Guid? selectedId = null)
    {
        if (!initialized)
            return;
        selectedId ??= (HistoryList.SelectedItem as ClipboardEntry)?.Id;
        var entries = history.Query(SearchBox.Text, PinnedFilter.IsChecked == true);
        if (IsCompact)
            entries = entries.Take(4).ToArray();
        HistoryList.ItemsSource = entries;
        HistoryList.SelectedItem = entries.FirstOrDefault(entry => entry.Id == selectedId) ?? entries.FirstOrDefault();
        EmptyList.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMessage.Text = !string.IsNullOrWhiteSpace(SearchBox.Text) ? "No matching clips"
            : PinnedFilter.IsChecked == true ? "No pinned clips" : "Your clipboard is empty";
        CountLabel.Text = $"{entries.Count} of {history.Count} clips   |   {history.PinnedCount} pinned";
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (!initialized)
            return;
        var entry = HistoryList.SelectedItem as ClipboardEntry;
        DetailText.Text = entry?.Text ?? "";
        SelectionMeta.Text = entry?.Details ?? "No selection";
        CopyButton.IsEnabled = entry is not null && monitor is not null;
        PinButton.IsEnabled = DeleteButton.IsEnabled = entry is not null;
        PinButton.ToolTip = entry?.IsPinned == true ? "Unpin clip" : "Pin clip";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, (string)PinButton.ToolTip);
        PinIcon.Foreground = entry?.IsPinned == true ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 126, 120)) : Foreground;
        EmptyPreview.Visibility = entry is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs args)
    {
        if (!initialized)
            return;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs args) => RefreshList();
    private void History_SelectionChanged(object sender, SelectionChangedEventArgs args) => UpdateSelection();
    private async void Copy_Click(object sender, RoutedEventArgs args) => await CopySelectionAsync();

    private async Task CopySelectionAsync()
    {
        if (HistoryList.SelectedItem is ClipboardEntry entry && monitor is not null)
            SetStatus(await monitor.CopyAsync(entry.Text) ? "Copied to clipboard" : "Clipboard is busy. Try copying again.");
    }

    private void History_MouseDown(object sender, MouseButtonEventArgs args)
    {
        pressedClip = (ItemsControl.ContainerFromElement(HistoryList, args.OriginalSource as DependencyObject)
            as ListBoxItem)?.DataContext is ClipboardEntry entry ? entry.Id : null;
    }

    private async void History_Click(object sender, MouseButtonEventArgs args)
    {
        var pressed = pressedClip;
        pressedClip = null;
        if (ItemsControl.ContainerFromElement(HistoryList, args.OriginalSource as DependencyObject) is not ListBoxItem item
            || item.DataContext is not ClipboardEntry entry || pressed != entry.Id)
            return;
        args.Handled = true;
        await PasteEntryAsync(entry);
    }

    internal async Task<bool> PasteEntryAsync(ClipboardEntry entry)
    {
        var target = pasteTarget;
        if (pasting || target is null || monitor is null || !IsVisible)
            return false;
        pasting = true;
        try
        {
            if (!await target.RestoreFocusAsync(new WindowInteropHelper(this).Handle) || !IsVisible || exitRequested)
                return false;
            if (!await monitor.CopyAsync(entry.Text))
            {
                SetStatus("Clipboard is busy. Try pasting again.");
                return false;
            }
            if (!IsVisible || exitRequested || !await target.SendPasteAsync())
                return false;
            pasteTarget = null;
            Hide();
            return true;
        }
        finally { pasting = false; }
    }

    private void Pin_Click(object sender, RoutedEventArgs args)
    {
        if (HistoryList.SelectedItem is not ClipboardEntry entry)
            return;
        if (!history.TogglePin(entry.Id))
        {
            SetStatus($"You can pin up to {ClipboardHistory.MaxPins} clips.");
            return;
        }
        RefreshList(entry.Id);
        ScheduleSave();
    }

    private void Delete_Click(object sender, RoutedEventArgs args)
    {
        if (HistoryList.SelectedItem is ClipboardEntry entry)
        {
            history.Remove(entry.Id);
            RefreshList();
            ScheduleSave();
            SetStatus("Clip deleted");
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs args)
    {
        var message = persistenceBlocked
            ? "Discard the unreadable history file and clear all clips, including pins? The current Windows clipboard is not changed."
            : "Delete all saved clips, including pins? The current Windows clipboard is not changed.";
        if (MessageBox.Show(this, message, "Clear history", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        history.Clear();
        persistenceBlocked = false;
        RefreshList();
        if (await SaveAsync())
            SetStatus("History cleared");
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs args)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void Wrap_Changed(object sender, RoutedEventArgs args)
    {
        if (DetailText is not null)
            DetailText.TextWrapping = WrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!initialized)
            return;
        var compact = ActualWidth < 760 || ActualHeight < 460;
        if (compact == IsCompact)
            return;
        IsCompact = compact;
        PreviewPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ListColumn.Width = new GridLength(compact ? 1 : 0.9, GridUnitType.Star);
        PreviewGap.Width = new GridLength(compact ? 0 : 16);
        PreviewColumn.Width = compact ? new GridLength(0) : new GridLength(1.1, GridUnitType.Star);
        RefreshList();
    }

    private void Minimize_Click(object sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs args) => WindowState =
        WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Hide_Click(object sender, RoutedEventArgs args) => Close();

    private void TogglePause()
    {
        paused = !paused;
        if (monitor is not null)
            monitor.IsPaused = paused;
        UpdatePauseState();
    }

    private void UpdatePauseState()
    {
        SetStatus(paused ? "Capture paused" : "Listening");
        if (tray is not null)
            tray.Text = paused ? "ClipBoard - capture paused" : "ClipBoard";
        if (trayPause is not null)
            trayPause.Text = paused ? "Resume capture" : "Pause capture";
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else Hide();
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
            && (SearchBox.IsKeyboardFocusWithin || HistoryList.IsKeyboardFocusWithin || DetailText.IsKeyboardFocusWithin))
        {
            await CopySelectionAsync();
            args.Handled = true;
        }
        else if (args.Key == Key.Down && SearchBox.IsKeyboardFocused && HistoryList.Items.Count > 0)
        {
            HistoryList.Focus();
            (HistoryList.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, HistoryList.SelectedIndex)) as ListBoxItem)?.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Delete && HistoryList.IsKeyboardFocusWithin)
        {
            Delete_Click(sender, args);
            args.Handled = true;
        }
    }

    private void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    private async Task<bool> SaveAsync()
    {
        saveTimer.Stop();
        if (persistenceBlocked)
            return true;
        var snapshot = history.Query();
        pendingSave = pendingSave.ContinueWith(_ => store.Save(snapshot), TaskScheduler.Default);
        try
        {
            await pendingSave;
            return true;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            SetStatus("Could not save history. Check disk space and folder permissions.");
            return false;
        }
    }

    public async void ShowFromTray() => await ShowFromTrayAsync();

    internal async Task ShowFromTrayAsync()
    {
        if (shortcutDialog is not null)
        {
            shortcutDialog.Activate();
            return;
        }
        if (openingWindow || pasting || exitRequested)
            return;
        openingWindow = true;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (PasteTarget.ForegroundWindow != handle)
                pasteTarget = await PasteTarget.CaptureAsync(handle);
            if (exitRequested)
                return;
            Show();
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;
            NativeWindowPlacement.MoveToCursor(this);
            Activate();
            NativeClipboardMonitor.Foreground(new WindowInteropHelper(this).Handle);
            SearchBox.Focus();
            SearchBox.SelectAll();
            UpdateSelection();
        }
        finally { openingWindow = false; }
    }

    protected override void OnStateChanged(EventArgs args)
    {
        base.OnStateChanged(args);
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        if (!exiting)
        {
            args.Cancel = true;
            Hide();
            if (tray is not null)
            {
                tray.BalloonTipTitle = "ClipBoard is still running";
                tray.BalloonTipText = $"Open it from the tray or press {shortcutSettings.Shortcut.Label}. Choose Exit in the tray menu to quit.";
                tray.ShowBalloonTip(3000);
            }
        }
        base.OnClosing(args);
    }

    public async Task ExitAsync()
    {
        if (exitRequested)
            return;
        exitRequested = true;
        IsEnabled = false;
        if (monitor is not null)
            monitor.IsPaused = true;
        var saved = await SaveAsync();
        if (!saved && MessageBox.Show(this, "The latest history could not be saved. Exit anyway?", "Save failed",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            exitRequested = false;
            IsEnabled = true;
            if (monitor is not null)
                monitor.IsPaused = paused;
            return;
        }
        exiting = true;
        retentionTimer.Stop();
        shortcuts?.Dispose();
        monitor?.Dispose();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        Close();
        Application.Current?.Shutdown();
    }

    private void SetStatus(string message) => StatusMessage.Text = message;

    private static bool IsStorageError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}