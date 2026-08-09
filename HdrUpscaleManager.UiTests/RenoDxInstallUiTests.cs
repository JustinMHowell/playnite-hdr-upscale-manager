using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Local-only integration test for the RenoDX install/uninstall round trip — triggered
       through the real per-game manager window UI (clicking the actual "Install"/"Uninstall"
       buttons), not by calling RenoDxService directly, so this exercises the same path a user
       would. Confirms the download+copy actually lands a file in the game folder (not just
       that a dialog said so), that the install record on disk matches it, and that
       uninstalling removes both — back to exactly the state before this test ran, since
       AddonInstallService never touches any pre-existing game file (there isn't one to
       back up — addon files aren't part of the original game).

       Runs against every game in TestGames below — each one confirmed on the RenoDX wiki's
       matched list with a direct .addon64 download (see GAMES-TO-TEST.md), so the plain
       "Install" button's best-effort wiki-match path is expected to succeed on its own,
       without needing the engine-specific fallback buttons. If your library titles one of
       these differently, edit the literal string in TestGames rather than adding an env var —
       there's no single "override" that makes sense once more than one game is covered.

       Also confirms RenoDX's ReShade dependency: RenoDX ships as a ReShade addon
       (.addon64/.addon32), which does nothing without ReShade already hosting it in the game
       folder (AddonInstallService's own doc comment says it drops the file "next to the
       ReShade DLL"). ManagerViewModel.EnsureReShadeInstalledForRenoDxAsync auto-installs
       ReShade first when it's missing — this regressed once already (added in 2ffa84d, silently
       dropped an hour later in bf9f2f4 when the old per-menu-item plugin files were deleted and
       consolidated into the manager window, confirmed live 2026-08-08), so this test asserts a
       ReShade proxy DLL actually lands in the game folder, not just the RenoDX addon file.

       What this doesn't cover: the wiki/manifest fetches in this codebase are all "try the
       network first, fall back to the on-disk cache only if that throws" (see
       RenoDxWikiService.FetchAllAsync) — there's no cache-first/skip-network path to exercise
       on a happy-path run. This test only confirms a fresh install does write/refresh that
       cache file; it doesn't simulate a network outage to exercise the fallback branch, since
       doing that reliably would mean blocking this machine's real network access, which is
       outside what an automated test here should do unattended. */
    [Collection(PlayniteUiCollection.Name)]
    public class RenoDxInstallUiTests
    {
        private const string RecordStoreFileName = "renodx_installs.json";
        private const string WikiCacheFileName = "renodx_wiki_mods.json";
        private const string WikiCacheDirName = "RenoDxWikiCache";

        private readonly ITestOutputHelper output;

        public RenoDxInstallUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // Maps each [InlineData] game name to its HUM_UITEST_{KEY}_GAME_ID/_INSTALL_DIR key —
        // see PLAN.md §9's scenario table for the real values to set locally.
        private static string ConfigKeyFor(string gameName) => gameName switch
        {
            "The Witcher 3: Wild Hunt" => "WITCHER3",
            "Hollow Knight" => "HOLLOWKNIGHT",
            _ => throw new ArgumentException($"No LocalTestGameConfig key mapped for '{gameName}' — add one.", nameof(gameName))
        };

        /* Deliberately NOT IClassFixture<PlayniteFixture>: that would share one Playnite
           process across every [InlineData] case, and a leftover open window or stale focus
           from one game's run bled into the next (confirmed live 2026-08-08 — Hollow Knight's
           case saw a manager window still open from the prior case, then its own navigation
           failed against that contaminated state). Each case now launches and tears down its
           own Playnite instance, same as ManifestAutoDownloadUiTests/StartupErrorDialogUiTests. */
        [Theory]
        [InlineData("The Witcher 3: Wild Hunt")]
        [InlineData("Hollow Knight")]
        public void InstallThenUninstallRenoDx_FromManagerWindow_DownloadsFileAndCleansUpFully(string gameName)
        {
            // Fails fast (Directory.Exists check) if this game isn't actually installed right
            // now, instead of only surfacing that several UI-driven steps later.
            LocalTestGameConfig.Resolve(ConfigKeyFor(gameName));

            var preExisting = FindRecordByGameName(gameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{gameName}' already has a tracked RenoDX install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs " +
                    "then uninstalls RenoDX as part of its own run, so it refuses to run " +
                    "against a game that already has a real install it didn't create itself — " +
                    "uninstall RenoDX for this game manually first.");
            }

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

            try
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "RenoDxInstall", output);

                var installedPath = WaitForInstall(gameName);
                Assert.True(File.Exists(installedPath),
                    $"RenoDX install wrote a record but the addon file isn't on disk at '{installedPath}'.");

                var gameInstallDirectory = Path.GetDirectoryName(installedPath);
                var reShadeProxyDllPath = AssertReShadeInstalled(gameInstallDirectory);

                AssertWikiCacheWasFreshlyFetched(testStartedUtc);

                ClickUninstallUntilRecordGone(managerWindow, gameName, installedPath);

                // The addon has no "Uninstall ReShade" action to drive through the UI (its own
                // status text says installs "always fetch the latest version" — untracked by
                // design), so deleting is the only way to leave the game folder as it was — but
                // only for a ReShade install THIS run actually triggered. A pre-existing ReShade
                // install (confirmed live 2026-08-08 — The Witcher 3 already had one from an
                // earlier, unrelated session) isn't this test's to remove. Checking
                // CreationTimeUtc, not LastWriteTimeUtc: ReShadeInstallService.InstallAsync
                // copies from a cached, previously-extracted DLL via File.Copy, which preserves
                // the SOURCE file's last-write time (confirmed live 2026-08-08 — a genuinely
                // fresh install on Hollow Knight still showed a days-old LastWriteTimeUtc
                // inherited from whenever that cached DLL was first extracted, wrongly reading
                // as "pre-existing" and leaking a real leftover). File.Copy always stamps a
                // fresh CreationTimeUtc on the destination, which is what actually answers "did
                // this run create this file at this path."
                if (File.GetCreationTimeUtc(reShadeProxyDllPath) >= testStartedUtc.AddSeconds(-2))
                {
                    File.Delete(reShadeProxyDllPath);
                }
            }
            finally
            {
                // A lingering modal error dialog (e.g. an install failure) disables the owner
                // window, and FlaUI's Close() throws MethodNotSupportedException against a
                // disabled window — confirmed live 2026-08-08, and it replaces whatever real
                // exception the try block was already throwing, hiding the actual failure
                // reason behind a useless "Close is not supported" instead. Best-effort only:
                // this is cleanup, not another assertion.
                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }

        private string WaitForInstall(string gameName)
        {
            RenoDxRecordDto record = null;
            var found = Retry.WhileFalse(
                () => (record = FindRecordByGameName(gameName)) != null,
                timeout: TimeSpan.FromSeconds(60),
                interval: TimeSpan.FromMilliseconds(500)).Result;

            if (!found)
            {
                output.WriteLine($"No RenoDX install record for '{gameName}' appeared within 60s.");
                output.WriteLine($"Record store checked: {PluginPaths.RecordStorePath(RecordStoreFileName)}");
                throw new InvalidOperationException(
                    "RenoDX install didn't complete within 60s of clicking Install — either the " +
                    "click didn't reach the command, the wiki fetch/download failed, or this " +
                    "game no longer has a matched RenoDX build. Check the manager window's " +
                    "status text or the plugin log for the real reason.");
            }

            return record.InstallPath;
        }

        /* The Uninstall button's IsEnabled is bound to a CanExecute that only becomes true once
           WPF's CommandManager re-queries it (RenoDxUninstallCommand's CanExecute checks
           RenoDxRecords.Get(GameId) != null) — and nothing in ManagerViewModel.Run() calls
           CommandManager.InvalidateRequerySuggested() after an install finishes. WPF normally
           re-queries on real input events, but there's no guarantee our own synthetic click's
           mouse-down is processed after that re-query rather than racing it — a disabled
           button doesn't fire Click at all, so a single click landing during that window is a
           silent no-op, not a real failure. Retrying the click a few times (each one is itself
           an input event that can trigger the re-query) resolves that race without needing to
           understand WPF's exact internal timing. */
        private void ClickUninstallUntilRecordGone(Window managerWindow, string gameName, string installedPath)
        {
            const int maxAttempts = 5;
            var recordGone = false;

            for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "RenoDxUninstall", output);
                recordGone = Retry.WhileFalse(
                    () => FindRecordByGameName(gameName) == null,
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
            }

            Assert.True(recordGone,
                $"RenoDX install record for '{gameName}' was still present after {maxAttempts} " +
                "Uninstall clicks.");

            var fileGone = Retry.WhileFalse(
                () => !File.Exists(installedPath),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(250)).Result;

            Assert.True(fileGone,
                $"Uninstall removed the tracked record but left the addon file behind at '{installedPath}' " +
                "— this game's install folder is no longer back to its pre-test state.");
        }

        /* ReShade's installer DLL gets renamed to whichever of these matches the game's
           detected graphics API (see Services/ReShade/ReShadeProxyDll.cs) — duplicated here
           rather than referenced, same as PluginPaths.PluginId, since this project deliberately
           has no reference back to the addon (see README.md). Deliberately NOT asserting this
           was freshly written: a game can legitimately already have ReShade installed from
           outside this test entirely (confirmed live 2026-08-08 — The Witcher 3 already had one
           from 2026-08-02), and EnsureReShadeInstalledForRenoDxAsync correctly leaves an
           existing install alone rather than reinstalling over it. The caller decides whether
           to clean it up based on whether THIS run actually wrote it. */
        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };

        private string AssertReShadeInstalled(string gameInstallDirectory)
        {
            var found = ReShadeProxyDllCandidates
                .Select(name => Path.Combine(gameInstallDirectory, name))
                .FirstOrDefault(File.Exists);

            if (found == null)
            {
                throw new InvalidOperationException(
                    $"RenoDX needs ReShade installed as a host, but none of " +
                    $"{string.Join(", ", ReShadeProxyDllCandidates)} exist in '{gameInstallDirectory}' " +
                    "after installing RenoDX — EnsureReShadeInstalledForRenoDxAsync didn't run, its " +
                    "auto-install failed silently, or ReShade genuinely isn't present for this game.");
            }

            return found;
        }

        /* Every wiki/manifest fetch in this codebase tries the network first and only reads
           the disk cache back if that throws (see the class doc comment) — so on a normal
           successful run, the cache file getting written/refreshed IS the "we fetched fresh
           data, not stale data" signal. Comparing against testStartedUtc (rather than just
           File.Exists) also catches "this is a leftover cache file from some earlier run
           that install silently reused without ever re-fetching." */
        private void AssertWikiCacheWasFreshlyFetched(DateTime testStartedUtc)
        {
            var cachePath = PluginPaths.CacheFilePath(WikiCacheDirName, WikiCacheFileName);
            Assert.True(File.Exists(cachePath), $"Expected the RenoDX wiki cache file at '{cachePath}' after a successful install.");

            var writtenAtUtc = File.GetLastWriteTimeUtc(cachePath);
            Assert.True(writtenAtUtc >= testStartedUtc.AddSeconds(-2),
                $"RenoDX wiki cache file at '{cachePath}' wasn't refreshed by this test run " +
                $"(last written {writtenAtUtc:u}, test started {testStartedUtc:u}) — install may " +
                "have reused stale cached data instead of fetching live.");
        }

        private static RenoDxRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, RenoDxRecordDto>>(File.ReadAllText(path));
                return all?.Values.FirstOrDefault(r =>
                    string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // A record store mid-write (rare, but Set()/Remove() aren't atomic) reads back
                // as invalid JSON for an instant — treat that the same as "not there yet" and
                // let the caller's Retry loop try again rather than fail the test on a fluke.
                return null;
            }
        }

        private class RenoDxRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
        }
    }
}
