using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipBoard;

internal static class NativeWindowPlacement
{
    public static void MoveToCursor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetCursorPos(out var cursor))
            return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, 2), ref monitor))
            return;
        var workArea = new Rect(monitor.Work.Left, monitor.Work.Top,
            monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
        for (var pass = 0; pass < 2; pass++)
        {
            if (!GetWindowRect(handle, out var bounds))
                return;
            var position = ClampToWorkArea(new Point(cursor.X, cursor.Y),
                new Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), workArea);
            if (!SetWindowPos(handle, 0, (int)position.X, (int)position.Y, 0, 0, 0x0015))
                return;
        }
    }

    internal static Point ClampToWorkArea(Point cursor, Size windowSize, Rect workArea) => new(
        Math.Clamp(cursor.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowSize.Width)),
        Math.Clamp(cursor.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowSize.Height)));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint handle, nint after, int left, int top, int width, int height, uint flags);
}