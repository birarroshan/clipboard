using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClipBoard.Core;

namespace ClipBoard;

internal sealed class GlobalShortcutService : IDisposable
{
    private const int FirstHotkeyId = 0x4342;
    private static readonly uint ActivateMessage = RegisterWindowMessage("ClipBoard.WinV.Activate.1");
    private readonly HwndSource source;
    private readonly Action activate;
    private readonly HookCallback callback;
    private readonly WinVKeyFilter filter = new();
    private nint keyboardHook;
    private int hotkeyId;
    private bool disposed;

    public ShortcutSettings Settings { get; private set; } = new() { OverrideWinV = false };
    public bool HotkeyRegistered => hotkeyId != 0;
    public bool WinVEnabled => Settings.OverrideWinV && keyboardHook != 0;

    public GlobalShortcutService(nint handle, Action activate)
    {
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window handle unavailable.");
        this.activate = activate;
        callback = KeyboardMessage;
        source.AddHook(WindowMessage);
    }

    public bool TryConfigure(ShortcutSettings requested, Action persist, out string error)
    {
        error = "";
        if (disposed)
        {
            error = "Shortcut service is closed.";
            return false;
        }
        try { requested.Validate(); }
        catch (InvalidDataException exception)
        {
            error = exception.Message;
            return false;
        }

        var replaceHotkey = !HotkeyRegistered || requested.Shortcut != Settings.Shortcut;
        var candidateId = hotkeyId == FirstHotkeyId ? FirstHotkeyId + 1 : FirstHotkeyId;
        if (replaceHotkey && !RegisterHotKey(source.Handle, candidateId,
            requested.Shortcut.Modifiers | 0x4000, requested.Shortcut.VirtualKey))
        {
            error = $"{requested.Shortcut.Label} is unavailable. Choose a different shortcut.";
            return false;
        }

        var installedHook = false;
        if (requested.OverrideWinV && keyboardHook == 0)
        {
            keyboardHook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (keyboardHook == 0)
            {
                if (replaceHotkey)
                    UnregisterHotKey(source.Handle, candidateId);
                error = "Windows could not enable the Win+V override. Your previous shortcut is unchanged.";
                return false;
            }
            installedHook = true;
        }

        try { persist(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            if (replaceHotkey)
                UnregisterHotKey(source.Handle, candidateId);
            if (installedHook)
                RemoveKeyboardHook();
            error = "Could not save shortcut settings. Your previous shortcut is unchanged.";
            return false;
        }

        if (replaceHotkey)
        {
            if (HotkeyRegistered)
                UnregisterHotKey(source.Handle, hotkeyId);
            hotkeyId = candidateId;
        }
        Settings = requested;
        if (!requested.OverrideWinV)
            RemoveKeyboardHook();
        return true;
    }

    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((message == 0x0312 && HotkeyRegistered && wParam == hotkeyId)
            || (message == ActivateMessage && WinVEnabled))
        {
            activate();
            handled = true;
        }
        return 0;
    }

    private nint KeyboardMessage(int code, nint message, nint data)
    {
        if (code >= 0 && WinVEnabled && message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
        {
            var key = Marshal.PtrToStructure<KeyboardHookData>(data);
            var keyDown = message is 0x0100 or 0x0104;
            if (filter.Process(key.VirtualKey, keyDown, IsDown(0x5B) || IsDown(0x5C),
                IsDown(0x10) || IsDown(0x11) || IsDown(0x12), out var shouldActivate))
            {
                if (shouldActivate && (!MaskWindowsKeyRelease() || !PostMessage(source.Handle, ActivateMessage, 0, 0)))
                {
                    filter.Reset();
                    return CallNextHookEx(keyboardHook, code, message, data);
                }
                return 1;
            }
        }
        return CallNextHookEx(keyboardHook, code, message, data);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static bool MaskWindowsKeyRelease()
    {
        Input[] inputs = [
            new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = 0xE8 } } },
            new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = 0xE8, Flags = 2 } } }
        ];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private void RemoveKeyboardHook()
    {
        if (keyboardHook != 0 && UnhookWindowsHookEx(keyboardHook))
            keyboardHook = 0;
        filter.Reset();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Settings = Settings with { OverrideWinV = false };
        RemoveKeyboardHook();
        if (HotkeyRegistered)
            UnregisterHotKey(source.Handle, hotkeyId);
        hotkeyId = 0;
        source.RemoveHook(WindowMessage);
    }

    private delegate nint HookCallback(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint handle, int id);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookEx(int type, HookCallback callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint handle, uint message, nint first, nint second);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}