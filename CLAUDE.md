# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A C# WinForms launcher/management tool for the football game *Championship Manager 01/02*. It bundles the game, multiple data updates, Nick's Patcher, an editor, and helper tools into a single self-contained executable so end users don't have to manually download files or fiddle with compatibility settings. See `README.md` for the full user-facing feature list and the meaning of every patcher option.

The shipped app runs on Windows (and a Wine-based Mac port). The source here is a Windows-only WinForms project.

## Build

- **Toolchain:** Visual Studio / MSBuild on Windows. Targets **.NET Framework 4.0 Client Profile**, output assembly `CM0102StarterKit.exe` (`OutputType` WinExe). There is no cross-platform build — `msbuild`/`dotnet` on this macOS dev machine will not produce a runnable artifact.
- **Configurations:** `Debug`/`Release` × `Any CPU`/`x86`/`x64`. The release pipeline uses **x86 Release**.
- **Single-exe packaging:** the csproj defines a custom `ILMerge` target that merges `ICSharpCode.SharpZipLib.dll` into `CM0102StarterKit.exe`. NuGet packages (`ILMerge`, `SharpZipLib`) are restored via package restore.
- **There are no automated tests, linters, or CI** in this repo.

## Binary assets ARE in this fork (differs from upstream)

Unlike the upstream repo (which gitignores them), **this fork tracks all binary assets** so a clean clone can rebuild the full exe. They are embedded as resources via `Properties/Resources.resx`:

- `data/*.zip` — the database/data-update payloads (`original_data`, `patched_data`, `october_data`, plus this fork's `may2026_data_patched` / `may2026_2526_data_patched`).
- `external/Game.zip` — the entire game tree extracted on first run. **Stored split** as `external/Game.zip.part-*` because it exceeds GitHub's 100 MB file limit; before building, reassemble it: `./external/reassemble-game-zip.sh` (or `cat external/Game.zip.part-* > external/Game.zip`). The joined file itself stays gitignored.
- `external/Files/*.exe` — the game executables (`cm0102.exe` plus year-specific variants `cm0102_oct.exe`, `cm0102_2025.exe`, `cm0102_2026.exe`, …). Note two placeholders: `cm89_retro.exe` is a copy of `cm89.exe` (the shipped v1.2.2 exe these assets were recovered from predates the real one) and `dotNetFx40setup.exe` is a zero-byte stub (Windows-only bootstrap, unused).
- `external/events_eng.cfg`, `images/*.jpg`, and `packages/` (SharpZipLib 0.86.0 + ILMerge props, force-added so no NuGet restore is needed).

Resource names referenced in code (e.g. `Resources.october_data`, `Resources.cm0102_exe`) map to files under these folders. The 2025/2026 game exes and May-2026 data zips were produced with the sibling `CM0102Patcher` fork's headless harness (see that repo's CLAUDE.md).

## Runtime layout

At runtime everything lives under a `Game/` folder next to the exe (`Helper.GameFolder = cwd/Game`). On first launch `MainMenu_Load` writes the embedded `Game.zip` resource to disk and extracts it (one-time, ~20–30s). Key subpaths are all defined as constants in `Helper.cs`: `Game/Data` (active database), `Game/Patches` (+ `Optional/`, `Misc/`), `Game/Custom Databases`, and the loader config files in `Game/` (`CM0102LoaderDefault.ini` / `CM0102LoaderCustom.ini`).

## Architecture

### Form navigation
- Entry point: `Program.cs` → `MainMenu` (a Form).
- This fork has TWO screens only: `MainMenu` and `VersionMenu` (Data Updates). The upstream `NickPatcherMenu`, `PlayMenu` and `AndroidMenu` forms were REMOVED (2026-07-08): the shipped GSLP databases bake all patcher options via `ConfigLines`, the Play button launches the game directly (Standard/default-ini path only), and the Android flow is gone.
- Navigation is `ShowNewScreen(form)` in `HidableForm`: hide current, reposition next form to the same location/size, show it. The "Back" button returns to `mainMenu`. Forms are never disposed/recreated.

### The `#if DEBUG` base-class swap (every menu form)
Each menu declares its base class conditionally:
```csharp
partial class XMenu
#if DEBUG
    : MiddleForm
#else
    : HidableForm
#endif
```
`HidableForm` is **abstract** (the real base: loader overlay, centred message boxes, progress windows, button enable/disable, screen switching). The Visual Studio form designer cannot instantiate an abstract base, so `MiddleForm` is a tiny concrete subclass used only at design/DEBUG time. When editing forms, keep this pattern intact.

### `Helper.cs` — central configuration hub
Static class (imported via `using static`) holding all path constants and the **database model**:
- `Database` objects (`OriginalDatabase`, `OctoberDatabasePatched`, `Cm89Database`, …) each bundle: display `Label`, embedded `DataFile` zip, `ExeFile`, a `DeleteDataFolder` flag, an optional `PrerequisiteDatabase` (extracted first, underneath), and a `ConfigLines` map.
- The active database is detected at runtime by a marker file `Game/Data/<database.Name>.txt` (`CurrentDatabase()`); a custom-loaded database falls back to `CustomDatabase`.

### Loader config files are line-number-indexed (key coupling/gotcha)
`CM0102LoaderDefault.ini` / `CM0102LoaderCustom.ini` are read/written **by 1-based line number**, not by key, via `Helper.ConfigLine` / the per-database `ConfigLines` dictionaries (`{ lineNumber → (name, value) }`). `VersionMenu.UpdateConfigFiles` rewrites both files on every database switch; unforced lines carry their previous values over, except line 16 (`PatchFileDirectory`) which is normalized back to "." in the default ini for databases that don't force it.
Year is special-cased: a forced custom year is written as `Year = 0` in the file (the loader must not re-shift a pre-patched exe).
**GSLP gotcha:** the GSLP exes ship with most Nick/Tapani patches PRE-BAKED by GS (coloured attributes, 9 subs, regen fixes, load-all-players, unprotected contracts). Their `ConfigLines` write these as `false` — re-applying loader patches on top of the modified exe corrupts it (e.g. the HiddenAttributes patch cave lands on GS's extended data tables → crash opening any player profile). Only byte-verified-clean patches are enabled (`HideNonPublicBids` + the `NoForeignRestrictionsForAll.patch` file, managed by `SetupDatabase`).

### Database switching
`VersionMenu.SetupDatabase` extracts the prerequisite (if any) then the chosen database zip into `Game/Data` (preserving `Fonts/`), writes the database detector file if the zip didn't contain one (the two GSLP databases share one zip), manages the GSLP patch files in `Game/Patches`, copies fonts, and rewrites both ini files. Shipped databases: `Patched (3.9.68)`, `25/26 (2026)` and `26/27 (2027)` (GSLP × May-2026 transplant, pre-patched exes) + save/load custom.

### Patches
Patch files (`*.patch`) live in `Game/Patches/Optional/` and `Game/Patches/Misc/`. The patcher copies the desired ones into `Game/Patches/` (which the loader auto-loads) and deletes them when unchecked. "Miscellaneous Patches" = copy all of `Patches/Misc/`. Detection on form load is presence-based (file exists → checkbox ticked; >6 patch files → Misc enabled).

### Launching the game
`MainMenu.PlayGame_Click` writes the current database's exe into `Game/cm0102.exe`, starts `CM0102Loader.exe` (a stub) with the Default ini as argument, waits on the spawned `cm0102` process, and on exit restores the stock exe. Other tools (editor, CM Scout, GPF2, CM Explorer) are launched via `RunExternalProcess`; CM Explorer is embedded as `cmexplorer.zip` and extracted to `Game/CMExplorer` on first use (needs uncompressed saves).

### Win32 interop
`CentreMessageBox.cs` uses P/Invoke (`EnumThreadWindows`, `MoveWindow`, …) to recentre native message boxes over the (non-modal) parent form. Windows-only by nature.

## Conventions
- Namespace `CM0102_Starter_Kit`; `*.Designer.cs` files are Visual-Studio-generated UI layout — edit through the designer, not by hand, where possible.
- Menus pull shared chrome via `InitialiseSharedControls(title, xLocation, backButtonEnabled)` (defined in `HidableForm.Designer.cs`) called before `InitializeComponent()`.
- Git history is overwhelmingly README/release updates; commits are short imperative summaries.
