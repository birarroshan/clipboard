using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipBoard.Core;
using Xunit;

namespace ClipBoard.Tests;

public sealed class DesktopSmokeTests
{
    [Fact]
    public async Task WindowSupportsCompactHistoryResponsivePreviewAndTrayRestore()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await ExerciseWindowAsync();
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
            window.ShowFromTray();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);

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

    private static void SaveScreenshot(Window window, string name)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
        Assert.True(output.Length > 10_000);
    }
}