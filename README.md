# DeskDuck

> Keep Spotify as background music until a configured app actually makes sound, then Spotify smoothly ducks and stays ducked until everything goes quiet again.

![Windows](https://img.shields.io/badge/platform-Windows-blue)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)

<!-- LOGO PLACEHOLDER: replace with a 200px-wide render of the app logo, e.g.
     <img src="DeskDuck/Assets/Icons/DeskDuck%20Logo%20(Transparent).png" width="200" alt="DeskDuck logo"> -->

<!-- SCREENSHOT PLACEHOLDER: main window at rest (NORMAL state), ~700px wide -->

<!-- DEMO PLACEHOLDER (optional but high-value): 10-20s GIF: play audio in a
     trigger app, show the status flipping to DUCKED and the target dipping -->

DeskDuck is a tiny Windows tray utility that works like a desktop-wide sidechain for your music. Spotify (or any music app) plays at full volume while you work. The moment FL Studio, Godot, Chrome, or any app you pick actually starts producing audio, DeskDuck dips the music out of the way and brings it back when things go quiet.

## How it works

DeskDuck watches Windows audio sessions, not process lists and not loudness. Opening FL Studio does nothing by itself. Pressing play, which flips its audio session to ACTIVE, ducks the music.

Two trigger modes:

- **Session activity**: duck while a trigger app's session is ACTIVE, at any loudness. Quiet ambience still holds the duck; no loudness flicker.
- **Audio level**: duck only while a trigger app is louder than your threshold (dB), with 3 dB hysteresis so it never flutters at the boundary.

Each duck runs an attack → hold → release envelope (defaults 100 / 400 / 800 ms), all adjustable. The pre-duck volume is captured on entry and restored on exit, including across target restarts and DeskDuck restarts, and closing the window hides to the system tray instead of quitting.

## Features

- Activity- and level-based ducking with hysteresis
- Per-session target volume control (master volume untouched)
- Attack / hold / release timing knobs, 1–100% duck depth
- Live session diagnostics (app, PID, state, level, role) with duck-cause highlighting
- Event log with auto-scroll and clear
- Close-to-tray with status tooltip, toggle, and quick actions
- Volume safety: pre-duck level retained and reconciled even if the target or DeskDuck restarts mid-duck
- Contextual help window (`?` in the header)

## Requirements

- Windows 10/11 (Win11 for titlebar tinting)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build)

No installer, no admin rights, no network, no account. Everything runs locally.

## Build and run

```powershell
dotnet build
Start-Process DeskDuck\bin\Debug\net10.0-windows\DeskDuck.exe
```

Or open `DeskDuck.slnx` in Visual Studio and press F5. Settings live in `%AppData%\DeskDuck\settings.json`.

## Usage

1. **WHAT TO DUCK**: pick your music app (usually Spotify).
2. **WHEN TO DUCK**: tick the apps that should dip it, then pick a trigger mode. In Audio level mode, set the threshold while watching the live Level column.
3. Flip the **Enabled** switch on. Done.

Closing the window minimizes to the tray (balloon tip on first hide). Right-click the tray icon to show, toggle, test-duck, or fully exit. `Alt+S` / `Alt+L` open Sessions / Log.

## Settings reference

| Key | Default | Range | Meaning |
|---|---|---|---|
| `Enabled` | `true` | n/a | Master switch; when off, volumes are never touched |
| `TargetProcessName` | `Spotify` | n/a | Music app to duck (`.exe` optional, case-insensitive) |
| `TriggerProcesses` | `["FL Studio", "Godot"]` | n/a | Apps allowed to trigger a duck |
| `TriggerMode` | `Activity` | `Activity` / `Level` | Session-state vs loudness triggering |
| `ThresholdDb` | `-30` | `-60` – `0` | Level-mode engage point (releases 3 dB lower) |
| `DuckFactor` | `0.25` | `0.01` – `1.00` | Ducked volume = normal × factor |
| `AttackMilliseconds` | `100` | `0` – `5000` | Fade time into the duck |
| `HoldMilliseconds` | `400` | `0` – `10000` | Quiet-wait before releasing |
| `ReleaseMilliseconds` | `800` | `0` – `10000` | Fade time back to normal |

Malformed files fall back to defaults: a bad edit never prevents startup.

## Project structure

```text
DeskDuck/
├── Audio/        # SessionMonitor, VolumeController (NAudio Core Audio)
├── Core/         # DuckEngine (NORMAL → DUCKING → DUCKED → HOLDING → RELEASING)
├── Models/       # AppSettings, session snapshots, process-name matching
├── Services/     # SettingsService, AppIconService, TrayManager
├── ViewModels/   # MainViewModel (UI bindings, trigger aggregation)
├── Views/        # Sessions, Log, Help windows + DWM tint helper
├── Controls/     # Rotary Knob (drag / wheel / arrows / double-click reset)
├── Resources/    # Styles.xaml (palette, Space Mono, control templates)
└── Assets/       # Space Mono fonts, logo, moodboard (reference only)
```

## Troubleshooting

- **Music never ducks**: Enabled on? Trigger ticked (and not the target itself)? In Level mode, is the threshold above the app's actual peaks (check the Level column)? Is the session actually ACTIVE (play pressed, not just open)?
- **Music stuck quiet**: fixed automatically. DeskDuck retains and reconciles the pre-duck volume across target and app restarts. If it ever happens again, the Log window says why.
- **FL Studio doesn't trigger**: depends on its audio driver settings (e.g. ASIO "Auto close" changes when Windows sees an active session). Watch the State column while pressing play/stop. That column is always the truth.
- **No sound apps listed**: check the Output line. The default render device may have changed; the monitor reinitializes automatically.

## Non-goals

No virtual drivers, no DSP/FFT analysis, no recording, no Spotify API or login, no network, no database, no cross-platform support. Small native utility on purpose.

## License

<!-- LICENSE PLACEHOLDER: no license file ships yet. Pick one (MIT is the
     usual default for a personal utility) and add LICENSE alongside this. -->
TBD: a license has not been chosen yet.
