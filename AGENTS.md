# AGENTS.md

Windows-only .NET 8 WinForms tray utility (C#). UI text, comments, and test messages are Spanish — keep new user-facing strings in Spanish.

## Commands

Use the SDK that `build.ps1` uses. `dotnet` from PATH resolves to `C:\Program Files\dotnet\dotnet.exe`, which has no SDK on this machine and fails with "No .NET SDKs were found".

```powershell
# Build
& "$env:USERPROFILE\.dotnet\dotnet.exe" build -c Debug

# Full release + single-file publish -> publish\ScreenDimmer.exe
.\build.ps1

# Run the test harness
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project tests
```

- Tests are a plain console executable with a custom `Assert` in `tests/Program.cs`, not xUnit/NUnit. `dotnet test` finds nothing; run them with `dotnet run --project tests`. Exit code is `0` all pass, `1` any fail.
- There is no `.sln`. The root `ScreenDimmer.csproj` excludes `tests/**`, so `dotnet build` at the root does not build the tests. Use `--project tests` explicitly.

## Architecture

- Entrypoint: `Program.cs`. Single instance is enforced with a global mutex; a second launch just shows a message box and exits.
- `Core/BrightnessController.cs` owns the dimming level (hard-clamped 0–85), enabled state, and engine. It dispatches to one of two engines:
  - `GammaController` (`DimmingEngine.Hardware`, default): scales the GPU gamma ramp. Preferred because overlays are bypassed by MPO/DirectFlip (Chrome/Edge scrolling, video, some dialogs).
  - `ScreenOverlay` (`DimmingEngine.Overlay`, fallback): layered, click-through Win32 window covering the virtual screen.
- **Safety-critical:** `GammaController` snapshots the original gamma ramp on startup. Every exit path and unhandled-exception handler must call `Restore()`/dispose it, or the user's display stays dimmed. See the existing handlers in `Program.cs`. Closing `MainForm` normally hides to the tray (does not exit); only `ExitApplication()` terminates.
- Windows resets the gamma ramp on monitor power-off/on, resolution changes, and resume. `MainForm` runs a 1.5 s watchdog calling `BrightnessController.ReapplyIfNeeded()`. Keep `SetDimming` applying unconditionally even when the level is unchanged — that early-return caused "UI says 80% but nothing is dimmed" bugs.
- MainForm UI is built in code in `BuildModernUi()` (`Forms/MainForm.cs`). `MainForm.Designer.cs` contains only form basics — add controls to `BuildModernUi`, not the designer.
- Settings persist to `%APPDATA%\ScreenDimmer\settings.json` and are saved after every UI change (`SettingsService`, temp-file + move). `SettingsService(string path)` accepts an alternate path for tests.
- Startup-to-Windows creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\ScreenDimmer.lnk` with `--minimized`; it intentionally does not touch the registry.

## Gotchas

- Profile percentages (Día 15 / Tarde 30 / Noche 50 / Lectura 65) and the `"Tarde"` default are asserted in `tests/Program.cs`; change them together.
- Manual UI testing while an instance is already in the tray hits the single-instance dialog. Kill `ScreenDimmer` first.
- `bin/`, `obj/`, `tests/bin/`, `tests/obj/`, and `publish/` are gitignored. `build.ps1` renames a locked `publish\ScreenDimmer.exe` to a `.bak` before publishing.
- Target is `net8.0-windows`; the project cannot build or run on non-Windows hosts.
