# SolidWorks Body Exporter

MVP add-in for reviewing SolidWorks Part bodies, saving body export metadata back into the `.SLDPRT` file, copying tabular data, and exporting `.xlsx`.

## Current MVP Scope

- SolidWorks COM add-in skeleton.
- Reads solid bodies from the active Part document.
- Calculates X/Y/Z body bounding-box sizes in millimeters.
- Defaults dimension mapping by size:
  - Largest axis = Length
  - Smallest axis = Thickness
  - Remaining axis = Width
- Allows users to edit body display names.
- Allows users to swap Length/Width/Thickness axis mapping.
- Does not allow users to edit numeric dimensions directly.
- Saves names and mapping into the Part custom property `SBE_BodyExportMetadata`.
- Shows rows as New, Unchanged, SizeChanged, or Deleted.
- Copies all rows as tab-separated text for Excel.
- Exports `.xlsx` through ClosedXML.

## Architecture (v0.6.x)

Two artefacts work together:

- `SolidWorksBodyExporter.AddIn.dll` - COM add-in loaded by SolidWorks at startup. Hosts the WPF window, the license check, the body scanner, the Excel exporter, AND a per-user **named pipe server** (`IpcServer`).
- `SolidWorksBodyExporter.Launcher.exe` - standalone Windows desktop executable. Pinned to the taskbar / Start Menu / desktop. Connects to the add-in's named pipe and asks it to open the WPF window.

The named pipe is the reliable entry point. SolidWorks 2024's ribbon paint loop refuses to honour the add-in's enable callback after the first paint cycle (we spent ~15 build cycles in v0.5.x proving this and trying every documented mitigation); telemetry confirmed the callback was being polled and was returning enabled, yet the ribbon kept rendering greyed out. Pipe IPC sidesteps the ribbon entirely.

## Launching Body Exporter

**Recommended: pin `SolidWorksBodyExporter.Launcher.exe` to the taskbar.**

Build output: `src/SolidWorksBodyExporter.Launcher/bin/Debug/net48/SolidWorksBodyExporter.Launcher.exe`

Usage:

1. Start SolidWorks 2024.
2. Make sure the Body Exporter add-in is enabled: Tools - Add-Ins... - check "SolidWorks Body Exporter".
3. Open a `.SLDPRT` document.
4. Click the pinned launcher shortcut.

What happens under the hood:

```
[Launcher.exe]  --(named pipe)-->  [SolidWorks process]
                                    + Body Exporter add-in
                                      + IpcServer (per-user pipe)
                                      + WPF BodyExportWindow
```

The launcher writes `OPEN` to the pipe, the add-in marshals the request onto the SolidWorks main thread, and `BodyExportWindow` opens as a modeless child of the SolidWorks main window. The launcher exe exits within a second; the body export window keeps living inside the SolidWorks process.

Failure modes are all surfaced as friendly MessageBoxes:

| Situation | Launcher behaviour |
|---|---|
| SolidWorks not running | "SolidWorks is not running... please start SolidWorks..." |
| SolidWorks running but add-in disabled | Same message - the pipe is hosted by the add-in, so no pipe means no add-in |
| Add-in loaded but no active Part document | Add-in shows "Open a Part document before running Body Exporter" |
| Second launcher click while a previous one is still running | Silently absorbed by a `Local\` named mutex |
| License tampered / wrong machine | License gate inside `ShowBodyExporter` blocks and surfaces the fingerprint |

### Ribbon icon (best-effort fallback)

The add-in still registers a Body Exporter command group and tab in the SolidWorks CommandManager. Wherever SolidWorks happens to render it correctly, clicking the icon opens exactly the same window as the launcher. We deleted the v0.5.x ribbon-stabilisation heuristics (30-second heartbeat timer, multi-stage `InvalidateRect` chain, registry `Tab Props` rewrites driven from a callback, etc.) because telemetry proved they did not work; what remains is the minimum CommandGroup wiring SolidWorks needs to discover the command, plus a one-time registry cleanup for orphan tabs from previous installs. If the ribbon icon greys, use the launcher - that path does not depend on it.

## Build Notes

This project targets `.NET Framework 4.8` because SolidWorks add-ins run through COM on Windows.

Build on a machine with:

- Visual Studio 2022
- .NET Framework 4.8 Developer Pack
- SolidWorks installed, including the SolidWorks interop assemblies

Open `SolidWorksBodyExporter.sln`, restore NuGet packages, then build.

If SolidWorks is installed in a different folder, pass the interop location:

```powershell
dotnet build .\SolidWorksBodyExporter.sln -p:SolidWorksInteropPath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
```

## Production License Warning

Google Sheets can be used only for a private prototype or admin spreadsheet. It should not be the license server for a paid plugin.

For production, use a real backend:

- ASP.NET Core API
- PostgreSQL or SQL Server
- Stripe, Paddle, or Lemon Squeezy webhooks
- Signed license tokens
- Machine activation limits
- Trial state stored server-side
- Short offline grace period

Important code and secrets must stay on the server. The SolidWorks add-in should only contain client UI, SolidWorks API integration, and signed-token verification logic.
