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

## Binary assets are not in the repo (important)

`data/` and `external/` are gitignored (see `.gitignore`) and absent from a clean checkout. They hold the large binaries the app embeds as resources via `Properties/Resources.resx`:

- `data/*.zip` — the 13 database/data-update payloads (`original_data`, `patched_data`, `october_data`, `cm89_data`, …).
- `external/Game.zip` — the entire game tree extracted on first run.
- `external/Files/*.exe` — the game executables (`cm0102.exe` plus year/era-specific variants like `cm0102_oct.exe`, `cm89_retro.exe`).
- `external/events_eng.cfg`, `external/dotNetFx40setup.exe`, etc.

A clean checkout **cannot build** without these. When working here you are almost always editing C# logic/UI, not the assets. Resource names referenced in code (e.g. `Resources.october_data`, `Resources.cm0102_exe`) map to files under those gitignored folders.

## Runtime layout

At runtime everything lives under a `Game/` folder next to the exe (`Helper.GameFolder = cwd/Game`). On first launch `MainMenu_Load` writes the embedded `Game.zip` resource to disk and extracts it (one-time, ~20–30s). Key subpaths are all defined as constants in `Helper.cs`: `Game/Data` (active database), `Game/Patches` (+ `Optional/`, `Misc/`), `Game/Custom Databases`, and the loader config files in `Game/` (`CM0102LoaderDefault.ini` / `CM0102LoaderCustom.ini`).

## Architecture

### Form navigation
- Entry point: `Program.cs` → `MainMenu` (a Form).
- `MainMenu` constructs all sub-menus as singletons (`nickPatcherMenu`, `versionMenu`, `playMenu`, `androidMenu`) and holds references to them; sub-menus hold a back-reference to `mainMenu`, which is how they call into each other (e.g. `NickPatcherMenu` calls `mainMenu.versionMenu.SetupDatabase(...)`).
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
`CM0102LoaderDefault.ini` / `CM0102LoaderCustom.ini` are read/written **by 1-based line number**, not by key. This coupling is spread across:
- `Helper.ConfigLine` / the per-database `ConfigLines` dictionaries (`{ lineNumber → (name, value) }`) and `AndroidConfigLines`.
- `NickPatcherMenu`'s `GetComboBoxes()`/`GetNumericUpDowns()`/`GetCheckBoxes()` which map each UI control to a specific line number, and `Apply_Click` which **rewrites the entire file as an ordered `List<string>`** in fixed line order.

If the loader ini format changes, all of these line numbers must move together. A database's `ConfigLines` entry both forces a value and **locks the corresponding UI control** (disabled + shown as set) — this is how era databases (1989/1993/etc.) pin the starting Year and hide options that are already baked into their custom exe. Year is special-cased: a forced custom year is written as `Year = 0` in the file.

### Database switching & year-specific datasets
`VersionMenu.SetupDatabase` extracts the prerequisite (if any) then the chosen database zip into `Game/Data` (preserving `Fonts/`), copies fonts, and rewrites both ini files. Some real-world databases need a different underlying dataset depending on the chosen **starting year** — `GetPatchedDatabase` swaps e.g. `OctoberDatabase` → `OctoberDatabasePatched` when year 2021 is selected, and `NickPatcherMenu.Apply_Click` re-runs `SetupDatabase` to switch datasets when the year changes. The Reading/Derby `PointsDeductions.patch` is added/removed based on this same condition.

### Patches
Patch files (`*.patch`) live in `Game/Patches/Optional/` and `Game/Patches/Misc/`. The patcher copies the desired ones into `Game/Patches/` (which the loader auto-loads) and deletes them when unchecked. "Miscellaneous Patches" = copy all of `Patches/Misc/`. Detection on form load is presence-based (file exists → checkbox ticked; >6 patch files → Misc enabled).

### Launching the game
`PlayMenu.PlayButton_Click` writes the appropriate exe into `Game/cm0102.exe`, then starts `CM0102Loader.exe` (a stub) with the ini filename as argument — Default ini for "Standard", Custom ini for "Nick's Patcher". It then waits on the spawned `cm0102` process, and on exit restores the default exe. Other tools (editor, CM Scout, GPF2) are launched directly via `RunExternalProcess`.

### Win32 interop
`CentreMessageBox.cs` uses P/Invoke (`EnumThreadWindows`, `MoveWindow`, …) to recentre native message boxes over the (non-modal) parent form. Windows-only by nature.

## Conventions
- Namespace `CM0102_Starter_Kit`; `*.Designer.cs` files are Visual-Studio-generated UI layout — edit through the designer, not by hand, where possible.
- Menus pull shared chrome via `InitialiseSharedControls(title, xLocation, backButtonEnabled)` (defined in `HidableForm.Designer.cs`) called before `InitializeComponent()`.
- Git history is overwhelmingly README/release updates; commits are short imperative summaries.
