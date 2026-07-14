# Championship Manager 01/02 Starter Kit — GSLP Mac fork

This is a personal fork of the [Championship Manager 01/02 Starter Kit](https://github.com/nckstwrt/CM0102-Starter-Kit) tailored for playing modern-day seasons on a Mac (via a Wine wrapper). It bundles the game, three ready-to-play databases with pre-patched executables, the official editor and a set of helper tools into a single self-contained app — no downloads, external patchers or compatibility fiddling needed.

Massive credit to the original authors: the Starter Kit itself, Nick (patcher and loader), Tapani and Saturn who pioneered the patching scene, the champman0102.net community for the data updates, and the Brazilian GSLP community whose 2023 update this fork's modern databases are built on.

## What's in this fork (vs upstream)

- **Three built-in databases** (see below) instead of upstream's ten, including two modern-season GSLP databases with May-2026 squads and pre-patched executables that start in 2025 or 2026.
- **Simplified interface**: the Nick's Patcher menu, Play submenu and Android support have been removed. All patcher options are baked per-database — you just pick a database and hit Play.
- **The Play button shows which database is loaded** (e.g. "Play 25/26 (2026)") so you always know which executable your save will run against.
- **CM Explorer bundled** as an in-app save-game browser/editor.
- All binary assets are tracked in the repo, so a clean clone rebuilds the complete exe (see `CLAUDE.md` for the build recipe; `external/Game.zip` ships split — run `./external/reassemble-game-zip.sh` first).

## The databases

Switch between databases from the **Data Updates** menu. Switching extracts the chosen database into `Game/Data` and rewrites the loader configuration — your save games are kept, but see the save-compatibility warning below.

- **Patched (3.9.68)** — the classic game with the community 3.9.68 patch, original 2001/02 season data. Starts in 2001.
- **25/26 (2026)** — GSLP-based modern database with May-2026 squads; starts in July 2025 and plays the 2025/26 season, including a hardcoded, correctly-drawn 2026 World Cup finals field (32 teams, USA/Mexico hosting).
- **26/27 (2027)** — the same world starting one season later, in July 2026.

Both modern databases use pre-patched executables with these enhancements already active (a mix of GS's baked-in patches, this fork's, and options the loader applies at launch):

- Coloured and **uncapped attributes** (values can exceed 20, up to 46)
- **Hidden attribute columns** on the squad screen (Consistency, Big Matches, Injury Proneness, Penalties, Ambition, Loyalty, Adaptability, Versatility and more)
- 9 substitutes named, load all players, regen fixes
- No work permits, no foreign player limits, unprotected contracts disabled, non-public bids hidden
- Game speed ×4 and 1280×800 resolution (best experienced in full-screen mode)

**Custom databases**: you can still save the currently-loaded database under your own name (e.g. after editing it with the Official Editor) and reload it later, or load any stock-format CM 01/02 database from a zip file. Custom databases run on the standard 3.9.68 executable, so they should start in 2001.

## ⚠ Save games and database switching

Saves embed their own database, but they must be run with the **matching executable** — and the loaded database decides which executable the Play button launches. So before loading a save, make sure the right database is active: a 2025-start save needs "25/26 (2026)" loaded, a 2026-start save needs "26/27 (2027)", and 2001-start saves need "Patched (3.9.68)". Loading a save against the wrong executable will crash or corrupt it. The Play button's label tells you which database is currently active.

## Main menu

- **Data Updates** — switch databases (see above).
- **Official Editor** — launches the official data editor with the right permissions/compatibility settings. Make sure it points at the Starter Kit's own `Game/Data` folder.
- **Play Game** — writes the active database's executable and launches the game through Nick's loader. The button label shows the active database.
- **Backup / Restore Save Games** — copies your saves to `CM0102 Backups` on the Desktop and back. Restoring overwrites same-named saves, so take care.
- **CM Scout** — launches CM Scout for finding players outside the in-game scouting. Some say it ruins the game a bit; use with caution!
- **Generated Player Finder** — launches GPF2, which tracks which past players your save's regens correspond to (run it against a save as early as possible).
- **Save Editor** — built-in editor for saved games (uncompressed saves only). Phase 1 edits club finances safely (values are written to every field the engine reads and clamped to overflow-safe ranges, with an automatic backup per save). For player/staff editing it can still launch the legacy CM Explorer 1.2 — a CM 00/01 tool, so treat that part as experimental, and keep money edits in the built-in editor.

## Mac notes

- The app is a Wine (Wineskin) wrapper around the Windows tool. After downloading, clear the quarantine flag **before unzipping**, or macOS will claim the app is damaged:
  `xattr -drs com.apple.quarantine ~/Downloads/<downloaded zip>`
  Then unzip (not with Keka!) and double-click the app. First launch takes 20–30 seconds while the `Game` folder is unpacked.
- To browse the game folder (saves, tactics, etc.), right-click the app → "Show Package Contents" → `drive_c/Program Files/Starter Kit v*.*.*/Game`.
- Play in **full-screen** mode: the modern databases run at 1280×800, which does not display properly windowed.
- Trackpad scrolling can run away to the bottom of a page; click-drag the scrollbars or use an external mouse.
- If you rebuild and reinstall the Starter Kit exe, fully quit and restart the app — a running instance keeps writing its own embedded game executables on every Play.

## Important notes

- Don't restart the game from the in-game main menu — it relaunches outside the Starter Kit and will demand the CD. Exit and launch again from the Starter Kit instead.
- Keep the patch situation of a save constant: the `Game/Patches` folder is managed automatically per database, so avoid manually adding or removing `.patch` files unless you know what you're doing (some patch files that work on the stock exe will corrupt the GSLP executables).
- Known cosmetic limitation of the 25/26 database: World Cup 2026 qualifier draw dates only show for Asia (the other zones' draws happened before the July-2025 game start and can't be retro-generated). The finals themselves play out correctly, and the 2030 cycle onwards is fully normal.

## Troubleshooting

For general Starter Kit issues, the upstream support thread and FAQ still apply: https://champman0102.net/viewtopic.php?f=43&t=2449

For issues specific to this fork (the GSLP databases, the Mac build), file an issue on this repo.
