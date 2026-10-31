# ClipBoard

A native Windows text clipboard manager with searchable history. Built with C# and WPF. No account, server, or cloud sync.

## Run

Open `artifacts\ClipBoard\ClipBoard.exe` after publishing. The self-contained Windows x64 build includes its own .NET runtime; keep the publish folder together. Windows may show a security warning because this development build is not code-signed.

To run from source, install the .NET 10 SDK on Windows and use:

```powershell
dotnet run --project src/ClipBoard/ClipBoard.csproj
```

To start with clipboard capture paused:

```powershell
dotnet run --project src/ClipBoard/ClipBoard.csproj -- --paused
```

## Use

- Copy text in any Windows application. ClipBoard keeps a local history while it is running.
- The window opens at 380 x 340 with small text and the top four clips, keeping pinned items first. It can resize down to 280 x 320. Drag the blank top strip to move it, or drag an edge or corner to resize. No title or pause control is shown in the window.
- Search by any part of the text, ignoring letter case. Search always covers the entire history; compact view shows the top four matching results. Use All clips or Pinned to filter.
- Resize to at least 760 x 460 to show the full results list and selected clip's preview. Shrinking below either threshold hides preview and restores the four-clip layout without deleting history.
- Click Copy, double-click an item, or press Enter from search, the list, or preview to copy its complete text. Paste normally in your destination application.
- Pin frequently used clips, delete individual clips, or clear all history with confirmation.
- Pause capture and Resume capture are available from the tray's right-click menu. Pause stops new capture without deleting existing history. Resume captures subsequent copies, not text copied while paused. Pause is session-only; use `--paused` to start paused.
- Closing or minimizing the window keeps the app in the system tray. Double-click the tray icon or press Win+V (the default) or Alt+B (the fallback) to reopen it. Right-click the tray icon and choose Exit to quit.
- Ctrl+F focuses search. Down from search moves to the result list. Delete removes a focused list item. Escape clears search, then hides the window.

The app does not simulate paste, launch links, or run copied commands. Starting a second instance reopens the existing one.

## Keyboard Shortcuts

- Win+V is enabled by default to open ClipBoard instead of Windows clipboard history. Turn off **Override Win+V** in the tray menu or shortcut dialog to restore Windows' built-in behavior. An explicitly saved opt-out is preserved across updates.
- Alt+B is the default fallback shortcut and remains available alongside Win+V. Right-click the tray icon and select **Keyboard shortcut (Alt+B)...** to change the fallback. Choose one or more of Ctrl, Alt, and Shift plus a letter, digit, or F1-F11. Reset defaults enables Win+V and restores Alt+B when you save.
- This override uses a low-level keyboard hook while ClipBoard is running, not a supported replacement for the Windows clipboard UI. Disabling it or exiting removes the hook and restores normal Win+V behavior. No Windows settings, registry policies, or startup entries are changed.
- Only unmodified Win+V is intercepted. Ctrl/Alt/Shift+Win+V, other Windows shortcuts, and ordinary typing pass through. Key repeat opens the window once; key presses are not recorded or logged. A nonprinting key event prevents the Start menu from opening when the Windows key is released.
- The override is best-effort: elevated applications, secure desktops, Remote Desktop, other remappers, or Windows removing a stalled hook can prevent it from working. Use the configured fallback or tray icon in those cases.
- Settings persist in `%LOCALAPPDATA%\ClipBoard\settings.json`, separately from encrypted clipboard history. Shortcut conflicts or failed saves leave the previous working configuration unchanged. If a saved shortcut is unavailable on startup, open the tray menu to choose another.

## Privacy And Limits

- Text only: images, file lists, and rich-text formatting are not stored. This is separate from Windows' built-in Win+V history and cannot import its existing entries.
- History is encrypted using Windows DPAPI for the current Windows user, at `%LOCALAPPDATA%\ClipBoard\history.dat`. Writes use encrypted temporary files and atomic replacement.
- The app makes no network requests and does not register itself to start with Windows.
- Up to 500 clips, including up to 100 pins. Unpinned clips expire after 30 days; pinned clips do not expire. Oldest unpinned clips are removed at capacity.
- Blank text and text longer than 131,072 UTF-16 code units are ignored. Recopying identical text refreshes its timestamp without creating duplicates or losing its pin.
- Capture respects `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory=0` when the source provides those hints. Not every password manager supplies them. Pause capture before copying sensitive information.
- Encryption does not protect against another process running as your Windows user. Clearing history does not clear the current Windows clipboard or guarantee forensic erasure.
- If a history file cannot be read, it is preserved and new captures remain in memory. Clear history explicitly to discard that file and re-enable saving. Save failures are shown in the status bar.

## Build And Test

```powershell
dotnet build ClipBoard.slnx
dotnet test ClipBoard.slnx
dotnet publish src/ClipBoard/ClipBoard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/ClipBoard
```

Tests cover search, Unicode, duplicate handling, pins, capacity, retention, encrypted persistence, corruption handling, and native window interactions, including compact defaults, four-clip limits, full-history search, and preview resize thresholds. Shortcut tests cover settings persistence, exact Win+V matching, repeat/release handling, native shortcut conflicts, failed-save rollback, hook installation/removal, and dialog validation. The WPF smoke test needs an interactive Windows desktop. It uses synthetic history with capture paused, does not replace your clipboard, and writes window and settings screenshots into `artifacts/screenshots`.

For a live integration check, launch the app, copy a disposable phrase from another application, search for it, copy it back, and paste into a scratch document. Also verify the global shortcut and tray menu on your machine. The automated tests do not alter the real clipboard to exercise this cross-application flow.

## Source Layout

- `src/ClipBoard.Core`: history rules and DPAPI storage.
- `src/ClipBoard`: native clipboard listener, WPF interface, tray, and hotkey.
- `tests/ClipBoard.Tests`: history, persistence, and native UI tests.

VS Code build/test tasks and a C# debugging configuration are included.