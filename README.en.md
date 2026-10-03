<div align="right">

[简体中文](README.md) | **English**

</div>

# WindowFocusMute — Auto-Mute Windows Apps on Focus Loss

> **Watching a video or in a meeting? Switch away and the app shuts up automatically.**

Watches the Windows foreground window: **processes on the list are muted automatically when their window loses focus, and unmuted when it regains focus.**

WinForms GUI, .NET 8, C#, **zero NuGet dependencies** (pure Win32 + COM interop).

## Features

- ✅ **Mute on focus loss / restore on focus**: polls the foreground window every 200 ms; listed processes are muted when you switch away and restored when you switch back
- ✅ **GUI**: tick processes in a list to apply instantly; shows each process's exe icon, PID and window title
- ✅ **Taskbar-accurate filtering**: by default only lists windows that actually appear on the taskbar (visible + non-tool-window + not DWM-cloaked); toggle to show all processes
- ✅ **Hot-reload config**: edit `targets.txt` manually and it reloads automatically, kept in sync with the checkboxes both ways
- ✅ **Smart restore**: removing a process from the list restores its audio; only the mute bit is toggled — system volume is never touched
- ✅ **Device resilient**: automatically rebuilds the audio-session controller when the default device changes or the COM session goes stale
- ✅ **Minimize to tray**: closing the window only tucks the app into the tray; exiting requires the tray menu, so the mute guard can't be closed by accident

## Usage

Run `WindowFocusMute.exe` (built to the project root), then:

- **Check a process** = add to the list; uncheck = remove. Changes are saved and take effect immediately.
- The list shows taskbar windows only by default; tick "Show all processes" to see everything.
- Processes that have exited stay listed as "(not running)" and remain checked, so the rule reapplies next time they start.
- The log area at the bottom shows every mute/restore action.

To find a process name: Task Manager → "Details" tab → "Name" column, without the `.exe` suffix.

**Closing the window = minimize to tray** (muting keeps running in the background). To actually quit, right-click the blue speaker icon in the tray → "Exit"; double-click the tray icon to bring the window back anytime.

## Configuration

`targets.txt` lives next to the exe, one process name per line (no `.exe`, case-insensitive, `#` starts a comment):

```
# WindowFocusMute target process list
citizen sleeper
msedge
spotify
```

Edit it by hand (auto-reloads on save) or just tick boxes in the UI.

## Building from Source

Requires the .NET 8 SDK:

```bash
dotnet build -c Release
```

Publish a single-file exe:

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false
```

The output lands in `bin\Release\net8.0-windows\publish\`; copying `WindowFocusMute.exe` alone is enough (the target machine needs the .NET 8 Desktop Runtime; use `-p:SelfContained=true` for a fully standalone, larger exe).

## Code Layout

```
Program.cs            Entry point: [STAThread] starts MuteEngine + MainForm
MainForm.cs           WinForms UI: process checklist, log area, hot-reload sync
MuteEngine.cs         Mute engine (pure logic, no UI): list I/O, focus polling, mute/restore
CoreAudioInterop.cs   WASAPI COM interop (all internal)
Win32.cs              Foreground-window PID + taskbar-standard window enumeration
app.ico               App icon
targets.txt           Runtime-maintained process list
```

### Implementation Notes

- **Engine and UI are fully decoupled**: the UI operates the list via `engine.Add/Remove/Targets` and receives notifications via the `Log` / `TargetsChanged` events — tray support or UI redesigns never touch the engine.
- **Focus detection**: 200 ms polling of `GetForegroundWindow` → `GetWindowThreadProcessId`; simple, reliable, no message hooks.
- **Muting**: Windows Core Audio (WASAPI) — `IAudioSessionManager2` enumerates all sessions on the default output device, `IAudioSessionControl2.GetProcessId` matches listed processes, `ISimpleAudioVolume.SetMute` toggles. Only the per-session mute bit changes.
- **Process list filtering**: `EnumWindows` with taskbar criteria (`IsWindowVisible` + non-empty title + not a tool window + no owner + not DWM-cloaked).

## Known Limitations (Possible Future Work)

- Works on the **default output device** only; all audio sessions of a process are handled together — no per-tab muting.
- Windows running elevated (as administrator) may not be resolvable to a process name (focus detection skips them).
- The 200 ms poll can feel slightly laggy on very fast switching; `SetWinEventHook` (event-driven) would be more responsive.
- A tray-minimized (windowless) process stays on the list, but muting only triggers once it has been focused and then switched away from.
- Todo candidates: start with Windows, restore mute state for listed processes on startup, multiple output devices.

## Requirements

- Windows 10/11 (x64)
- .NET 8 Desktop Runtime (to run); .NET 8 SDK (to build)

## License

MIT
