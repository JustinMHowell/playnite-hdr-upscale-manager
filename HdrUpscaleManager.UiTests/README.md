# HDR & Upscale Manager — local UI automation tests

FlaUI-driven smoke and integration tests that drive a **real Playnite** instance. There are
four test classes:

| Class | What it checks |
|---|---|
| `ManagerWindowUiTests` | Opening the per-game manager window renders every component section. UI shape only — installs nothing. |
| `ManifestSyncUiTests` | Clicking "Check manifest sync" in the main menu re-fetches `manifest.json` on disk. |
| `ManifestAutoDownloadUiTests` | A **fresh Playnite launch with no local `manifest.json`** re-downloads it automatically via `OnApplicationStarted`, with no click involved. The counterpart to `ManifestSyncUiTests` — see that class's own doc comment for why the two don't share a fixture. |
| `RenoDxInstallUiTests` | A real RenoDX install/uninstall round trip through the manager window's actual buttons: the addon file lands in the game folder, the on-disk install record matches it, and uninstalling removes both, leaving the game folder exactly as it was. Defaults to "The Witcher 3: Wild Hunt" (see `GAMES-TO-TEST.md`) — override with `HUM_UITEST_RENODX_GAME`. |

All four run against a real Playnite process, so they're serialized into one xUnit collection
(`PlayniteUiCollection`, `DisableParallelization = true`) — otherwise xUnit's default of running
different test classes in parallel would have two of them fighting to kill/launch the same
process at once. Run the whole project or a single class; either way they execute one at a time.

`RenoDxInstallUiTests` actually downloads a real file into the target game's install folder and
writes to your real Playnite plugin data under `%AppData%\Playnite\ExtensionsData\...` — it isn't
a pure UI smoke test like the others. It refuses to run if that game already has a tracked RenoDX
install going in, so it won't touch a real install you made outside of this test suite.

## Isolation from the addon

- This project has no `ProjectReference` to `HdrUpscaleManager.csproj`, and nothing in the
  addon or its build (`build-and-run.ps1`, `.github/workflows/build.yaml`, `addon_manifest.json`)
  references this project back. FlaUI is a dependency of this test project only — it is never
  restored, built, or shipped as part of the addon.
- There's no solution file wiring it into a "build everything" step; CI builds
  `HdrUpscaleManager.csproj` directly and never touches this folder.

## Prerequisites

1. Deploy the addon build and have Playnite running with your real library loaded:
   ```pwsh
   .\build-and-run.ps1
   ```
2. Pick a game already in your library that you're comfortable opening the manager window for.

## Required environment variable

- `HUM_UITEST_GAME` — the **exact** title of that game as Playnite displays it, required by
  `ManagerWindowUiTests` only, e.g.:
  ```pwsh
  $env:HUM_UITEST_GAME = 'Cyberpunk 2077'
  ```
  Never commit a specific value for this — it's your local library, set it in your shell before
  running the tests, not in a file.

## Optional environment variables

- `HUM_UITEST_RENODX_GAME` — the exact title of the game `RenoDxInstallUiTests` installs/
  uninstalls RenoDX against. Defaults to `The Witcher 3: Wild Hunt` if unset (see
  `GAMES-TO-TEST.md` for why that one). Only set this if your library titles it differently, or
  to point the test at a different game already confirmed on the RenoDX wiki's matched list.
- `HUM_UITEST_PLAYNITE_EXE` — full path to `Playnite.DesktopApp.exe`. Only needed if Playnite
  isn't already running *and* it's not installed at the default
  `%LOCALAPPDATA%\Playnite\Playnite.DesktopApp.exe` location. If Playnite is already running,
  the tests attach to it instead of launching a new instance.

## Running

```pwsh
$env:HUM_UITEST_GAME = 'Cyberpunk 2077'
dotnet test HdrUpscaleManager.UiTests
```

Or run `ManagerWindowUiTests` from the Test Explorer in Visual Studio/Rider.

## If a selector breaks

Playnite's own automation tree isn't something this repo can pin against — it's not our source,
and it can change between Playnite versions/themes. The game-list lookup and context-menu clicks
in `ManagerWindowUiTests.cs` are best-effort name matches. When one stops matching, the test
dumps the live automation tree (via `AutomationTreeDumper`) to the xunit test output right before
failing — paste that into the relevant `FindFirstDescendant`/`FindAllChildren` call and adjust
the one selector that broke.

## What this deliberately doesn't do

- `ManagerWindowUiTests` doesn't install anything, click any install/uninstall button, or touch
  your actual game files — it only opens the window and checks the section labels are present,
  then closes it. `RenoDxInstallUiTests` is the exception — see the table above.
- None of these run in CI. They require a real desktop session, a real Playnite install, and
  (for the game-specific ones) your own library — none of which exist on a CI runner.
- `RenoDxInstallUiTests` doesn't simulate a network outage to exercise the wiki-cache fallback
  path (`RenoDxWikiService.FetchAllAsync` falls back to its on-disk cache only when the live
  fetch throws) — it only confirms a successful install fetches fresh data rather than reusing a
  stale cache file. Actually forcing that fallback branch would mean blocking this machine's real
  network access, which is out of scope for an automated test here.
