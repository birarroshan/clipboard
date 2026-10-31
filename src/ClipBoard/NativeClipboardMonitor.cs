using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipBoard.Core;

namespace ClipBoard;

internal sealed class NativeClipboardMonitor : IDisposable
{
    private const int ClipboardUpdate = 0x031D;
    private const int HotkeyMessage = 0x0312;
    private const int HotkeyId = 0x4342;
    private const uint UnicodeText = 13;
    private static readonly uint ShowMessage = RegisterWindowMessage("ClipBoard.Show.1");
    private static readonly uint ExcludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private readonly HwndSource source;
    private readonly DispatcherTimer retryTimer;
    private readonly Action<string> captured;
    private readonly Action showWindow;
    private readonly Action<string> status;
    private uint ignoredSequence;
    private int attempts;
    private bool copying;
    private bool disposed;

    public bool IsPaused { get; set; }
    public bool HotkeyRegistered { get; }

    public NativeClipboardMonitor(nint handle, Action<string> captured, Action showWindow,
        Action<string> status, bool startPaused)
    {
        this.captured = captured;
        this.showWindow = showWindow;
        this.status = status;
        IsPaused = startPaused;
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window handle unavailable.");
        retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        retryTimer.Tick += (_, _) => ReadClipboard();
        if (!AddClipboardFormatListener(handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        source.AddHook(WindowMessage);
        HotkeyRegistered = RegisterHotKey(handle, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x56);
        if (!startPaused)
            retryTimer.Start();
    }

    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == ClipboardUpdate && !IsPaused && !copying)
        {
            attempts = 0;
            retryTimer.Start();
        }
        else if ((message == HotkeyMessage && wParam == HotkeyId) || message == ShowMessage)
        {
            showWindow();
            handled = true;
        }
        return 0;
    }

    private void ReadClipboard()
    {
        retryTimer.Stop();
        if (disposed || IsPaused || copying || GetClipboardSequenceNumber() == ignoredSequence)
            return;

        if (!OpenClipboard(source.Handle))
        {
            if (++attempts < 8)
                retryTimer.Start();
            else
                status("Clipboard is busy. The next copy will be captured.");
            return;
        }

        string? text = null;
        try
        {
            if (IsClipboardFormatAvailable(ExcludeFormat) || !IsClipboardFormatAvailable(UnicodeText))
                return;

            if (IsClipboardFormatAvailable(HistoryFormat))
            {
                var hint = GetClipboardData(HistoryFormat);
                var hintPointer = GlobalLock(hint);
                if (hintPointer == 0)
                    return;
                try
                {
                    if (GlobalSize(hint) < 4 || Marshal.ReadInt32(hintPointer) == 0)
                        return;
                }
                finally { GlobalUnlock(hint); }
            }

            var data = GetClipboardData(UnicodeText);
            var bytes = GlobalSize(data);
            if (bytes > (ClipboardHistory.MaxTextLength + 1) * 2)
            {
                status("Text exceeds the 128K-character limit and was skipped.");
                return;
            }
            if (data == 0 || bytes < 2)
                return;

            var pointer = GlobalLock(data);
            if (pointer == 0)
                return;
            try
            {
                text = Marshal.PtrToStringUni(pointer, (int)bytes / 2);
                var terminator = text?.IndexOf('\0') ?? -1;
                if (terminator >= 0)
                    text = text![..terminator];
            }
            finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }

        if (text is not null)
            captured(text);
    }

    public async Task<bool> CopyAsync(string text)
    {
        for (var attempt = 0; attempt < 5 && !disposed; attempt++)
        {
            copying = true;
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                ignoredSequence = GetClipboardSequenceNumber();
                return true;
            }
            catch (ExternalException)
            {
                await Task.Delay(60);
            }
            finally { copying = false; }
        }
        return false;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        retryTimer.Stop();
        RemoveClipboardFormatListener(source.Handle);
        if (HotkeyRegistered)
            UnregisterHotKey(source.Handle, HotkeyId);
        source.RemoveHook(WindowMessage);
    }

    public static void ShowExistingInstance() => PostMessage((nint)0xffff, ShowMessage, 0, 0);
    public static void Foreground(nint handle) => SetForegroundWindow(handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(nint handle);
    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(nint handle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint handle, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(nint handle);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint handle);
}