using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
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

       Defaults to "The Witcher 3: Wild Hunt" — confirmed on the RenoDX wiki's matched list
       with a direct .addon64 download (see GAMES-TO-TEST.md), so the plain "Install" button's
       best-effort wiki-match path is expected to succeed on its own, without needing the
       engine-specific fallback buttons. Override with HUM_UITEST_RENODX_GAME if your library
       titles it differently, or to point this at a different already-matched game.

       What this doesn't cover: the wiki/manifest fetches in this codebase are all "try the
       network first, fall back to the on-disk cache only if that throws" (see
       RenoDxWikiService.FetchAllAsync) — there's no cache-first/skip-network path to exercise
       on a happy-path run. This test only confirms a fresh install does write/refresh that
       cache file; it doesn't simulate a network outage to exercise the fallback branch, since
       doing that reliably would mean blocking this machine's real network access, which is
       outside what an automated test here should do unattended. */
    [Collection(PlayniteUiCollection.Name)]
    public class RenoDxInstallUiTests : IClassFixture<PlayniteFixture>
    {
        private const string RecordStoreFileName = "renodx_installs.json";
        private const string WikiCacheFileName = "renodx_wiki_mods.json";
        private const string WikiCacheDirName = "RenoDxWikiCache";

        private readonly PlayniteFixture fixture;
        private readonly ITestOutputHelper output;

        public RenoDxInstallUiTests(PlayniteFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        [Fact]
        public void InstallThenUninstallRenoDx_FromManagerWindow_DownloadsFileAndCleansUpFully()
        {
            var gameName = Environment.GetEnvironmentVariable("HUM_UITEST_RENODX_GAME");
            if (string.IsNullOrWhiteSpace(gameName)) gameName = "The Witcher 3: Wild Hunt";

            var preExisting = FindRecordByGameName(gameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{gameName}' already has a tracked RenoDX install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs " +
                    "then uninstalls RenoDX as part of its own run, so it refuses to run " +
                    "against a game that already has a real install it didn't create itself — " +
                    "uninstall RenoDX for this game manually first, or point " +
                    "HUM_UITEST_RENODX_GAME at a different, currently-clean matched game.");
            }

            var testStartedUtc = DateTime.UtcNow;
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

            try
            {
                var renoDxSection = FindSection(managerWindow, "RenoDX");

                ClickButton(renoDxSection, "Install");

                var installedPath = WaitForInstall(gameName);
                Assert.True(File.Exists(installedPath),
                    $"RenoDX install wrote a record but the addon file isn't on disk at '{installedPath}'.");

                AssertWikiCacheWasFreshlyFetched(testStartedUtc);

                ClickUninstallUntilRecordGone(renoDxSection, gameName, installedPath);
            }
            finally
            {
                managerWindow.Close();
            }
        }

        private AutomationElement FindSection(Window managerWindow, string title)
        {
            var heading = Retry.WhileNull(
                () => managerWindow.FindFirstDescendant(cf => cf.ByName(title)),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (heading == null)
            {
                Dump("manager window", managerWindow);
                throw new InvalidOperationException(
                    $"Couldn't find the '{title}' section heading in the manager window. See " +
                    "the dumped tree above to fix the selector.");
            }

            // The title TextBlock's immediate parent StackPanel is the whole section (title,
            // status, and every button) — see UI/ManagerView.xaml. Scoping button lookups to
            // this subtree is what avoids matching e.g. Luma's "Install" button instead.
            return heading.Parent ?? heading;
        }

        private void ClickButton(AutomationElement section, string buttonName)
        {
            var button = Retry.WhileNull(
                () => section.FindFirstDescendant(cf => cf.ByName(buttonName)),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (button == null)
            {
                Dump("RenoDX section", section);
                throw new InvalidOperationException(
                    $"Couldn't find a '{buttonName}' button in the RenoDX section. See the " +
                    "dumped tree above to fix the selector.");
            }

            Mouse.Click(button.GetClickablePoint(), MouseButton.Left);
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
        private void ClickUninstallUntilRecordGone(AutomationElement renoDxSection, string gameName, string installedPath)
        {
            const int maxAttempts = 5;
            var recordGone = false;

            for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
            {
                ClickButton(renoDxSection, "Uninstall");
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

        private void Dump(string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }

        private class RenoDxRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
        }
    }
}
