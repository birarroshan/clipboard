using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace ClipBoard;

internal sealed class PasteTarget(nint window, AutomationElement control)
{
    internal static nint ForegroundWindow => GetForegroundWindow();

    public static async Task<PasteTarget?> CaptureAsync(nint clipboardWindow)
    {
        var foreground = ForegroundWindow;
        if (foreground == 0 || foreground == clipboardWindow)
            return null;
        try
        {
            return await Task.Run(() =>
            {
                var focused = AutomationElement.FocusedElement;
                return focused is not null && IsEditable(focused) && BelongsToWindow(focused, foreground)
                    && ForegroundWindow == foreground ? new PasteTarget(foreground, focused) : null;
            }).WaitAsync(TimeSpan.FromMilliseconds(750));
        }
        catch (Exception exception) when (IsAutomationError(exception) || exception is TimeoutException)
        {
            return null;
        }
    }

    public Task<bool> RestoreFocusAsync(nint clipboardWindow) => Task.Run(() =>
    {
        try
        {
            if (ForegroundWindow != clipboardWindow || !IsWindow(window) || IsIconic(window)
                || !IsEditable(control) || !BelongsToWindow(control, window) || ModifiersDown())
                return false;
            if (ForegroundWindow != clipboardWindow || !SetForegroundWindow(window))
                return false;
            control.SetFocus();
            return HasFocus();
        }
        catch (Exception exception) when (IsAutomationError(exception)) { return false; }
    });

    public Task<bool> SendPasteAsync() => Task.Run(() =>
    {
        try
        {
            if (!HasFocus() || ModifiersDown() || ForegroundWindow != window)
                return false;
            Input[] keys = [Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true)];
            var sent = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
            if (sent > 0 && sent < keys.Length)
            {
                Input[] release = [Key(0x56, true), Key(0x11, true)];
                SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            }
            return sent == keys.Length;
        }
        catch (Exception exception) when (IsAutomationError(exception)) { return false; }
    });

    private bool HasFocus()
    {
        if (ForegroundWindow != window || !IsEditable(control))
            return false;
        var focused = AutomationElement.FocusedElement;
        return focused is not null && Automation.Compare(control, focused) && ForegroundWindow == window;
    }

    private static bool IsEditable(AutomationElement element)
    {
        var info = element.Current;
        bool? readOnly = null;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            readOnly = ((ValuePattern)value).Current.IsReadOnly;
        else if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text)
            && ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool attribute)
            readOnly = attribute;
        return AcceptsTextInput(info.ControlType, info.IsEnabled, info.IsOffscreen, readOnly);
    }

    internal static bool AcceptsTextInput(ControlType type, bool enabled, bool offscreen, bool? readOnly) =>
        enabled && !offscreen && readOnly == false && (type == ControlType.Edit || type == ControlType.Document);

    private static bool BelongsToWindow(AutomationElement element, nint expectedWindow)
    {
        for (var depth = 0; depth < 32; depth++)
        {
            var handle = (nint)element.Current.NativeWindowHandle;
            if (handle != 0)
                return GetAncestor(handle, 2) == expectedWindow;
            element = TreeWalker.ControlViewWalker.GetParent(element);
            if (element is null)
                return false;
        }
        return false;
    }

    private static bool ModifiersDown() => GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x11) < 0
        || GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;

    private static bool IsAutomationError(Exception exception) => exception is ElementNotAvailableException
        or ElementNotEnabledException or InvalidOperationException or COMException or UnauthorizedAccessException;

    private static Input Key(ushort key, bool released = false) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = released ? 2u : 0u } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}