[![Build, Test, and Publish](https://github.com/birarroshan/clipboard/actions/workflows/build.yml/badge.svg)](https://github.com/birarroshan/clipboard/actions/workflows/build.yml)
# ClipBoard

A native Windows text clipboard manager with searchable history and pinned clips. Built with C# and WPF. No account, server, or cloud sync.

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
- Opening places the top-left corner at the mouse pointer where possible, moving it nearby to stay inside that monitor's working area. A maximized window is restored first; a window larger than the working area is aligned to its top-left.
- Search by any part of the text, ignoring letter case. Search always covers the entire history; compact view shows the top four matching results. Use All clips or Pinned to filter.
- Resize to at least 760 x 460 to show the full results list and selected clip's preview. Shrinking below either threshold hides preview and restores the four-clip layout without deleting history.
- Focus the destination text field, open ClipBoard with its shortcut, and click a clip once to paste its full text at the existing caret or selection. ClipBoard hides to the tray after sending paste. If no supported editable text control was focused, the target closed or became read-only, or focus cannot be restored, clicking does not copy or hide.
- Click Copy or press Enter from search, the list, or preview to copy without pasting or hiding. Paste normally in your destination application. Select clips with the keyboard to inspect their preview without pasting.
- Pin frequently used clips, delete individual clips, or clear all history with confirmation.
- Pause capture and Resume capture are available from the tray's right-click menu. Pause stops new capture without deleting existing history. Resume captures subsequent copies, not text copied while paused. Pause is session-only; use `--paused` to start paused.
- Closing or minimizing the window keeps the app in the system tray. Double-click the tray icon or press Win+V (the default) or Alt+B (the fallback) to reopen it. Right-click the tray icon and choose Exit to quit.
- Ctrl+F focuses search. Down from search moves to the result list. Delete removes a focused list item. Escape clears search, then hides the window.

Click-to-paste uses Windows UI Automation to identify the focused editable control and sends Ctrl+V only after restoring and checking its focus. Unsupported editors, elevated applications, secure desktops, or Windows input restrictions can prevent paste; use Copy and paste manually instead. Opening through the tray may leave no editable target focused. The app does not launch links or directly execute copied commands; pasted text is handled by the destination application. Starting a second instance reopens the existing one.

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
dotnet publish src/ClipBoard/ClipBoard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/ClipBoard
```

Tests cover search, Unicode, duplicate handling, pins, capacity, retention, encrypted persistence, corruption handling, and native window interactions, including compact defaults, four-clip limits, full-history search, and preview resize thresholds. Shortcut tests cover settings persistence, exact Win+V matching, repeat/release handling, native shortcut conflicts, failed-save rollback, hook installation/removal, and dialog validation. The WPF smoke test needs an interactive Windows desktop. It uses synthetic history with capture paused, does not replace your clipboard, and writes window and settings screenshots into `artifacts/screenshots`.

Cursor-placement tests cover monitor edges, negative coordinates, scaled dimensions, and oversized windows. Paste-policy tests reject disabled, hidden, read-only, non-text, and unknown targets.

An opt-in paste test requires an unlocked desktop. It moves the pointer, activates scratch windows, temporarily writes synthetic text to the clipboard, and verifies a real clip click, selection replacement, hide-on-success, and invalid-target safeguards. It restores the prior pointer position and clipboard afterward. Keep other applications idle while running it:

```powershell
dotnet test tests/ClipBoard.Tests/ClipBoard.Tests.csproj -c Release --filter FullyQualifiedName~DesktopSmokeTests --environment CLIPBOARD_INTERACTIVE_TESTS=1
```

For a cross-application check, focus a scratch document in your usual editor, open ClipBoard with Win+V or the fallback shortcut, and click a disposable clip. Verify the insertion point, tray hiding, and placement on each monitor. Actual mixed-DPI displays, elevated applications, and editors with nonstandard accessibility support still need manual verification.

## GitHub Actions

[Build, Test, and Publish](.github/workflows/build.yml) runs on pushes to `main`, pull requests targeting `main`, version tags beginning with `v`, and manual runs. It uses a Windows runner with .NET 10 to restore, build, run the default tests (including the native WPF smoke test, but excluding the opt-in paste test), and publish two Windows x64 variants.

Successful builds upload a `ClipBoard-win-x64` artifact containing eight files: a versioned EXE, ZIP, and a SHA-256 checksum for each, for both variants:

- `ClipBoard-VERSION-win-x64-standalone.exe`: compressed, self-contained build. No .NET installation is needed. Compression reduces download size with some startup overhead.
- `ClipBoard-VERSION-win-x64-framework-dependent.exe`: much smaller build requiring the **.NET 10 Desktop Runtime for Windows x64**. The ASP.NET Core runtime or base .NET runtime alone is not sufficient. Install it from the [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0) page under **.NET Desktop Runtime**.

Each ZIP includes its variant's executable, README, and license. Download them from the workflow run's **Artifacts** section. Build packages are retained for 30 days; test results and synthetic UI screenshots are retained for 14 days. The native tests require Windows desktop APIs; actual shortcut focus, elevated-app behavior, and cross-application clipboard use still need a manual check.

## Create A Release

1. Commit and push the workflow and documentation to `main`. In GitHub, ensure Actions is enabled under **Settings > Actions > General**, and wait for the `main` build to pass. For the **Run workflow** button to be available, the workflow must be on the repository's default branch; set that branch to `main` under **Settings > General** if necessary.
2. Tag the tested commit and push that tag. Use a new `vMAJOR.MINOR.PATCH` tag for each release:

	```powershell
	git switch main
	git pull --ff-only origin main
	git tag -a v1.0.0 -m "ClipBoard 1.0.0"
	git push origin v1.0.0
	```

3. Open **Actions > Build, Test, and Publish** and wait for the tag run to succeed. It rebuilds and tests the tagged commit, uses `1.0.0` as the application/package version, and creates a **draft** release with `ClipBoard-1.0.0-win-x64-standalone.exe` and `ClipBoard-1.0.0-win-x64-framework-dependent.exe`, plus a ZIP for each variant and a `.sha256` file for every EXE and ZIP (eight assets total). The built-in `GITHUB_TOKEN` supplies release permissions; no personal access token or repository secret is needed. Organization policies must allow the release job's `contents: write` permission.
4. Open the repository's **Releases** page, edit the draft, review the generated notes and download both variants for a quick manual check, then click **Publish release**. Choose standalone when .NET is not installed; framework-dependent requires the .NET 10 Desktop Runtime x64. Users can run the EXE directly or extract the ZIP; GitHub's automatically generated source-code archives do not contain the built app. These builds are unsigned, so Windows may display a security warning.

For the next release, use a new tag such as `v1.0.1`; do not move or reuse a published version tag. Re-running a tag workflow uses the workflow from that tagged commit, so workflow changes require a new tag on a commit containing those changes. Re-runs can update an existing draft's assets, but refuse to replace assets on an already published release. A manual workflow run builds artifacts only; pushing a version tag is what creates a release draft. Release assets remain available beyond the build-artifact retention period.

To verify a downloaded asset, compare `Get-FileHash .\ClipBoard-1.0.0-win-x64-standalone.exe -Algorithm SHA256` with its downloaded `.exe.sha256` file. The same check applies to the framework-dependent EXE and both ZIPs using their matching `.sha256` files.

## Source Layout

- `src/ClipBoard.Core`: history rules and DPAPI storage.
- `src/ClipBoard`: native clipboard listener, WPF interface, tray, and hotkey.
- `tests/ClipBoard.Tests`: history, persistence, and native UI tests.

VS Code build/test tasks and a C# debugging configuration are included.
