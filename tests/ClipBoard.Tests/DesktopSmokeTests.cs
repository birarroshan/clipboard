using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipBoard.Core;
using Xunit;

namespace ClipBoard.Tests;

public sealed class DesktopSmokeTests
{
    [Theory]
    [InlineData(120, 80, 0, 0, 1920, 1040, 380, 340, 120, 80)]
    [InlineData(1900, 1030, 0, 0, 1920, 1040, 380, 340, 1540, 700)]
    [InlineData(-10, 900, -1920, 0, 1920, 1040, 380, 340, -380, 700)]
    [InlineData(3195, 770, 1920, -200, 1280, 984, 380, 340, 2820, 444)]
    [InlineData(100, 100, 0, 0, 320, 240, 380, 340, 0, 0)]
    [InlineData(1900, 1000, 0, 0, 1920, 1040, 760, 680, 1160, 360)]
    public void CursorPlacementStaysInMonitorWorkArea(double cursorX, double cursorY,
        double left, double top, double width, double height, double windowWidth, double windowHeight,
        double expectedX, double expectedY)
    {
        Assert.Equal(new Point(expectedX, expectedY), NativeWindowPlacement.ClampToWorkArea(
            new Point(cursorX, cursorY), new Size(windowWidth, windowHeight), new Rect(left, top, width, height)));
    }

    [Fact]
    public void PasteRequiresAnEnabledEditableTextControl()
    {
        var edit = System.Windows.Automation.ControlType.Edit;
        var document = System.Windows.Automation.ControlType.Document;
        Assert.True(PasteTarget.AcceptsTextInput(edit, true, false, false));
        Assert.True(PasteTarget.AcceptsTextInput(document, true, false, false));
        Assert.False(PasteTarget.AcceptsTextInput(edit, true, false, true));
        Assert.False(PasteTarget.AcceptsTextInput(document, true, false, true));
        Assert.False(PasteTarget.AcceptsTextInput(edit, false, false, false));
        Assert.False(PasteTarget.AcceptsTextInput(edit, true, true, false));
        Assert.False(PasteTarget.AcceptsTextInput(edit, true, false, null));
        Assert.False(PasteTarget.AcceptsTextInput(System.Windows.Automation.ControlType.Button, true, false, false));
    }

    [Fact]
    public async Task WindowSupportsCompactHistoryResponsivePreviewAndTrayRestore() =>
        await RunOnDispatcherAsync(ExerciseWindowAsync);

    [InteractivePasteFact]
    public async Task ClickPastesAtSelectionAndRejectsMissingOrReadOnlyTargets() =>
        await RunOnDispatcherAsync(ExercisePasteAsync);

    private sealed class InteractivePasteFactAttribute : FactAttribute
    {
        public InteractivePasteFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("CLIPBOARD_INTERACTIVE_TESTS") != "1")
                Skip = "Set CLIPBOARD_INTERACTIVE_TESTS=1 on an unlocked desktop to test real focus and paste.";
        }
    }

    private static async Task RunOnDispatcherAsync(Func<Task> exercise)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await exercise();
                    completion.SetResult();
                }
                catch (Exception exception) { completion.SetException(exception); }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static async Task ExerciseWindowAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClipBoard.UI.Tests", Guid.NewGuid().ToString("N"));
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        var now = DateTimeOffset.UtcNow;
        store.Save([
            new(Guid.NewGuid(), "Sprint notes\n\nReview the desktop build and confirm keyboard navigation.\n\nRelease checklist\n- Verify search and pinned clips\n- Check encrypted local storage\n- Package the Windows application", now, true),
            new(Guid.NewGuid(), "https://learn.microsoft.com/dotnet/desktop/wpf/", now - TimeSpan.FromMinutes(3)),
            new(Guid.NewGuid(), "dotnet publish src/ClipBoard/ClipBoard.csproj -c Release", now - TimeSpan.FromMinutes(8)),
            new(Guid.NewGuid(), "Design review\nThursday, 10:30 AM\nFocus: search, selection, and keyboard navigation.", now - TimeSpan.FromMinutes(21)),
            new(Guid.NewGuid(), "SELECT name, created_at\nFROM saved_items\nORDER BY created_at DESC;", now - TimeSpan.FromHours(1))
        ]);
        var window = new MainWindow(store, startPaused: true);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var search = (TextBox)window.FindName("SearchBox");
            var list = (ListBox)window.FindName("HistoryList");
            var detail = (TextBox)window.FindName("DetailText");
            var copy = (Button)window.FindName("CopyButton");
            var pin = (Button)window.FindName("PinButton");
            var pinned = (RadioButton)window.FindName("PinnedFilter");
            var all = (RadioButton)window.FindName("AllFilter");
            var preview = (Border)window.FindName("PreviewPane");
            Assert.Equal("ClipStack", window.Title);
            Assert.Equal("ClipStack", typeof(MainWindow).Assembly.GetName().Name);
            Assert.Equal(256, Assert.IsAssignableFrom<BitmapSource>(window.Icon).PixelWidth);
            Assert.Equal(380, window.Width);
            Assert.Equal(340, window.Height);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.True(window.IsCompact);
            Assert.Equal(Visibility.Collapsed, preview.Visibility);
            Assert.Equal(4, list.Items.Count);
            Assert.Equal(11, search.FontSize);
            Assert.Null(window.FindName("PauseButton"));
            Assert.Null(window.FindName("CaptureState"));
            Assert.Contains("Sprint notes", detail.Text);
            var selected = Assert.IsType<ClipboardEntry>(list.SelectedItem);
            Assert.StartsWith(selected.Timestamp, ((TextBlock)window.FindName("SelectionMeta")).Text);
            Assert.True(copy.IsEnabled);
            Assert.False(await window.PasteEntryAsync(selected));
            Assert.True(window.IsVisible);
            SaveScreenshot(window, "compact.png");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, "minimum.png");
            Assert.True(window.IsCompact);
            Assert.Equal(4, list.Items.Count);
            var fourth = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(3));
            Assert.True(fourth.TranslatePoint(new Point(0, fourth.ActualHeight), list).Y <= list.ActualHeight);
            Assert.True(copy.TranslatePoint(new Point(copy.ActualWidth, copy.ActualHeight), window).X <= window.ActualWidth);

            search.Text = "SELECT name";
            Assert.Single(list.Items.Cast<object>());
            Assert.Contains("SELECT name", detail.Text);
            search.Text = "DOTNET";
            Assert.Equal(2, list.Items.Count);
            search.Text = "not present anywhere";
            Assert.Empty(list.Items);
            Assert.False(copy.IsEnabled);
            Assert.Equal("No matching clips", ((TextBlock)window.FindName("EmptyMessage")).Text);
            search.Clear();
            Assert.Equal(4, list.Items.Count);

            window.Width = 1060;
            window.Height = 740;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsCompact);
            Assert.Equal(Visibility.Visible, preview.Visibility);
            Assert.True(detail.IsVisible);
            Assert.True(detail.ActualWidth > 200);
            Assert.Equal(5, list.Items.Count);
            SaveScreenshot(window, "desktop.png");

            window.Width = 760;
            window.Height = 459;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(window.IsCompact);
            Assert.False(detail.IsVisible);
            window.Height = 460;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsCompact);
            Assert.Equal(5, list.Items.Count);
            window.Width = 759;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(window.IsCompact);
            Assert.Equal(4, list.Items.Count);
            window.Width = 760;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            pinned.IsChecked = true;
            Assert.Single(list.Items.Cast<object>());
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(list.Items);
            all.IsChecked = true;
            Assert.Equal(5, list.Items.Count);

            ((Button)window.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, list.Items.Count);
            ((CheckBox)window.FindName("WrapToggle")).IsChecked = false;
            Assert.Equal(TextWrapping.NoWrap, detail.TextWrapping);

            window.WindowState = WindowState.Minimized;
            Assert.False(window.IsVisible);
            await window.ShowFromTrayAsync();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);

            await VerifyShortcutSettingsAsync(window, directory);
            await window.ExitAsync();
            Assert.Equal(4, store.Load().Count);
        }
        finally
        {
            await window.ExitAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ExercisePasteAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClipBoard.Paste.Tests", Guid.NewGuid().ToString("N"));
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        var entry = new ClipboardEntry(Guid.NewGuid(), "pasted text", DateTimeOffset.UtcNow);
        store.Save([entry]);
        new ShortcutSettingsStore(Path.Combine(directory, "settings.json"))
            .Save(new ShortcutSettings { OverrideWinV = false });
        var editor = new TextBox { Text = "before OLD after", Margin = new Thickness(10) };
        var button = new Button { Content = "Non-text target", Margin = new Thickness(10) };
        var panel = new StackPanel();
        panel.Children.Add(editor);
        panel.Children.Add(button);
        var target = new Window { Title = "ClipBoard paste test", Width = 320, Height = 180, Content = panel };
        var window = new MainWindow(store, startPaused: true);
        var originalClipboard = System.Windows.Clipboard.GetDataObject();
        var restorePointer = GetCursorPosition(out var originalPointer);
        try
        {
            target.Show();
            ActivateTestWindow(target);
            Assert.True(editor.Focus());
            editor.Select(7, 3);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await window.ShowFromTrayAsync();
            Assert.True(window.IsActive);
            Assert.True(window.HasPasteTarget, "The focused scratch text box was not captured before opening.");
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var keys = new System.Collections.Generic.List<string>();
            editor.PreviewKeyDown += (_, args) => keys.Add($"{args.Key}/{System.Windows.Input.Keyboard.Modifiers}");
            editor.TextChanged += (_, _) => changed.TrySetResult();
            window.UpdateLayout();
            var list = (ListBox)window.FindName("HistoryList");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var clickPosition = item.PointToScreen(new Point(item.ActualWidth / 2, item.ActualHeight / 2));
            Assert.True(SetCursorPosition((int)clickPosition.X, (int)clickPosition.Y));
            MouseEvent(0x0002, 0, 0, 0, 0);
            MouseEvent(0x0004, 0, 0, 0, 0);
            try { await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                Assert.Fail($"No text change: focused={editor.IsKeyboardFocused}; active={target.IsActive}; "
                    + $"foreground={PasteTarget.ForegroundWindow == new WindowInteropHelper(target).Handle}; "
                    + $"canPaste={System.Windows.Input.ApplicationCommands.Paste.CanExecute(null, editor)}; keys={string.Join(",", keys)}");
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsVisible);
            Assert.Equal("before pasted text after", editor.Text);

            System.Windows.Clipboard.SetText("unchanged marker");
            ActivateTestWindow(target);
            Assert.True(button.Focus());
            await window.ShowFromTrayAsync();
            Assert.False(await window.PasteEntryAsync(entry));
            Assert.True(window.IsVisible);
            Assert.Equal("unchanged marker", System.Windows.Clipboard.GetText());

            ActivateTestWindow(target);
            Assert.True(editor.Focus());
            await window.ShowFromTrayAsync();
            editor.IsReadOnly = true;
            Assert.False(await window.PasteEntryAsync(entry));
            Assert.True(window.IsVisible);
            Assert.Equal("unchanged marker", System.Windows.Clipboard.GetText());

            editor.IsReadOnly = false;
            ActivateTestWindow(target);
            Assert.True(editor.Focus());
            await window.ShowFromTrayAsync();
            target.Close();
            Assert.False(await window.PasteEntryAsync(entry));
            Assert.True(window.IsVisible);
            Assert.Equal("unchanged marker", System.Windows.Clipboard.GetText());
        }
        finally
        {
            try
            {
                if (originalClipboard is null)
                    System.Windows.Clipboard.Clear();
                else
                    System.Windows.Clipboard.SetDataObject(originalClipboard, true);
            }
            finally
            {
                if (restorePointer)
                    SetCursorPosition(originalPointer.X, originalPointer.Y);
                target.Close();
                await window.ExitAsync();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void ActivateTestWindow(Window window)
    {
        KeyboardEvent(0x12, 0, 0, 0);
        KeyboardEvent(0x12, 0, 2, 0);
        Assert.True(window.Activate(), "The interactive test needs foreground access on an unlocked desktop.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeyboardEvent(byte key, byte scan, uint flags, nuint extraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetCursorPosition(out System.Drawing.Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    private static extern bool SetCursorPosition(int left, int top);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint left, uint top, uint data, nuint extraInfo);

    private static async Task VerifyShortcutSettingsAsync(MainWindow window, string directory)
    {
        var settingsStore = new ShortcutSettingsStore(Path.Combine(directory, "settings.json"));
        Assert.Equal("Alt+B", settingsStore.Load().Shortcut.Label);
        Assert.True(settingsStore.Load().OverrideWinV);
        using var competingWindow = new HwndSource(new HwndSourceParameters("ClipBoard shortcut test") { Width = 1, Height = 1 });
        using var competingShortcut = new GlobalShortcutService(competingWindow.Handle, () => { });
        ShortcutSettings? occupied = null;
        ShortcutSettings? working = null;
        foreach (var key in Enumerable.Range(0x70, 11).Reverse())
        {
            var candidate = new ShortcutSettings { Shortcut = new ShortcutGesture(7, (uint)key), OverrideWinV = false };
            if (occupied is null && competingShortcut.TryConfigure(candidate, () => { }, out _))
                occupied = candidate;
            else if (occupied is not null && window.TryApplyShortcutSettings(candidate, out _))
            {
                working = candidate;
                break;
            }
        }
        Assert.NotNull(occupied);
        Assert.NotNull(working);
        Assert.Equal(working, settingsStore.Load());
        Assert.False(window.TryApplyShortcutSettings(occupied, out var error));
        Assert.Contains("unavailable", error);
        Assert.Equal(working, settingsStore.Load());
        competingShortcut.Dispose();

        using (var lockedFile = new FileStream(settingsStore.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(window.TryApplyShortcutSettings(occupied with { OverrideWinV = true }, out error));
            Assert.Contains("Could not save", error);
        }
        Assert.Equal(working, settingsStore.Load());
        using (var probe = new GlobalShortcutService(competingWindow.Handle, () => { }))
        {
            Assert.True(probe.TryConfigure(occupied with { OverrideWinV = true }, () => { }, out error), error);
            Assert.True(probe.WinVEnabled);
            Assert.False(probe.TryConfigure(working, () => { }, out error));
            Assert.True(probe.WinVEnabled);
            probe.Dispose();
            Assert.False(probe.WinVEnabled);
            Assert.False(probe.HotkeyRegistered);
        }

        Assert.True(window.TryApplyShortcutSettings(working with { OverrideWinV = true }, out error), error);
        Assert.True(settingsStore.Load().OverrideWinV);
        Assert.True(window.TryApplyShortcutSettings(working, out error), error);
        Assert.False(settingsStore.Load().OverrideWinV);

        ShortcutSettings? attempted = null;
        var dialog = new ShortcutSettingsWindow(new ShortcutSettings(), requested =>
        {
            attempted = requested;
            return "Shortcut unavailable.";
        }) { Owner = window, Resources = window.Resources };
        try
        {
            dialog.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var alt = (CheckBox)dialog.FindName("AltToggle");
            var control = (CheckBox)dialog.FindName("ControlToggle");
            var key = (ComboBox)dialog.FindName("KeySelector");
            var save = (Button)dialog.FindName("SaveButton");
            var message = (TextBlock)dialog.FindName("ErrorMessage");
            Assert.True(alt.IsChecked);
            Assert.True(((CheckBox)dialog.FindName("OverrideToggle")).IsChecked);
            Assert.Equal(0x42u, key.SelectedValue);
            alt.IsChecked = false;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(attempted);
            Assert.Equal(Visibility.Visible, message.Visibility);
            control.IsChecked = true;
            key.SelectedValue = 0x56u;
            ((CheckBox)dialog.FindName("OverrideToggle")).IsChecked = true;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(attempted);
            Assert.Equal("Ctrl+V", attempted.Shortcut.Label);
            Assert.True(attempted.OverrideWinV);
            Assert.Equal("Shortcut unavailable.", message.Text);
            SaveScreenshot(dialog, "shortcuts.png", 1_000);
        }
        finally { dialog.Close(); }
    }

    private static void SaveScreenshot(Window window, string name, int minimumBytes = 10_000)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(background);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
        Assert.True(output.Length > minimumBytes);
    }
}