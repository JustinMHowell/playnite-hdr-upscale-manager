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
    /* Local-only integration test for the Luma install/uninstall round trip — same shape as
       RenoDxInstallUiTests, see that class's doc comment for the reasoning that carries over
       unchanged (real UI buttons not direct service calls, per-Theory-case isolated Playnite
       instance, no cache-first path to exercise). The one structural difference is cleanup:
       Luma can deploy several files from a zip (see LumaInstalledRecord.InstalledFiles), so
       "file is gone" here means every installed file is gone, not just one.

       Runs against every game in the [InlineData] list below — Vanquish and INSIDE, both
       confirmed on the Luma wiki's matched list (see GAMES-TO-TEST.md) and both small,
       already-owned installs rather than something fresh to download just for this test. */
    [Collection(PlayniteUiCollection.Name)]
    public class LumaInstallUiTests
    {
        private const string RecordStoreFileName = "luma_installs.json";
        private const string WikiCacheFileName = "luma_wiki_mods.json";
        private const string WikiCacheDirName = "LumaWikiCache";

        private readonly ITestOutputHelper output;

        public LumaInstallUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("Vanquish")]
        [InlineData("INSIDE")]
        public void InstallThenUninstallLuma_FromManagerWindow_DownloadsFilesAndCleansUpFully(string gameName)
        {
            var preExisting = FindRecordByGameName(gameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{gameName}' already has a tracked Luma install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs " +
                    "then uninstalls Luma as part of its own run, so it refuses to run against " +
                    "a game that already has a real install it didn't create itself — uninstall " +
                    "Luma for this game manually first.");
            }

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

            try
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "LumaInstall", output);

                var record = WaitForInstall(gameName);
                Assert.NotEmpty(record.InstalledFiles);
                foreach (var relativePath in record.InstalledFiles)
                {
                    var fullPath = Path.Combine(record.InstallPath, relativePath);
                    Assert.True(File.Exists(fullPath),
                        $"Luma install wrote a record listing '{relativePath}' but it isn't on disk at '{fullPath}'.");
                }

                AssertWikiCacheWasFreshlyFetched(testStartedUtc);

                ClickUninstallUntilRecordGone(managerWindow, gameName, record);
            }
            finally
            {
                // See RenoDxInstallUiTests' own finally block for why this can't be a bare
                // Close() call: a lingering modal error dialog disables the owner window, and
                // FlaUI's Close() throws against a disabled window, masking whatever real
                // exception the try block was already throwing.
                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }

        private LumaRecordDto WaitForInstall(string gameName)
        {
            LumaRecordDto record = null;
            var found = Retry.WhileFalse(
                () => (record = FindRecordByGameName(gameName)) != null,
                timeout: TimeSpan.FromSeconds(60),
                interval: TimeSpan.FromMilliseconds(500)).Result;

            if (!found)
            {
                output.WriteLine($"No Luma install record for '{gameName}' appeared within 60s.");
                output.WriteLine($"Record store checked: {PluginPaths.RecordStorePath(RecordStoreFileName)}");
                throw new InvalidOperationException(
                    "Luma install didn't complete within 60s of clicking Install — either the " +
                    "click didn't reach the command, the wiki fetch/download failed, or this " +
                    "game no longer has a matched Luma build. Check the manager window's " +
                    "status text or the plugin log for the real reason.");
            }

            return record;
        }

        /* See RenoDxInstallUiTests.ClickUninstallUntilRecordGone for why a single click isn't
           reliable — same WPF CommandManager requery race, same fix. */
        private void ClickUninstallUntilRecordGone(Window managerWindow, string gameName, LumaRecordDto record)
        {
            const int maxAttempts = 5;
            var recordGone = false;

            for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "LumaUninstall", output);
                recordGone = Retry.WhileFalse(
                    () => FindRecordByGameName(gameName) == null,
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
            }

            Assert.True(recordGone,
                $"Luma install record for '{gameName}' was still present after {maxAttempts} " +
                "Uninstall clicks.");

            foreach (var relativePath in record.InstalledFiles)
            {
                var fullPath = Path.Combine(record.InstallPath, relativePath);
                var fileGone = Retry.WhileFalse(
                    () => !File.Exists(fullPath),
                    timeout: TimeSpan.FromSeconds(15),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                Assert.True(fileGone,
                    $"Uninstall removed the tracked record but left '{relativePath}' behind at " +
                    $"'{fullPath}' — this game's install folder is no longer back to its pre-test state.");
            }
        }

        /* Every wiki/manifest fetch in this codebase tries the network first and only reads
           the disk cache back if that throws — see RenoDxInstallUiTests' own doc comment for
           why a freshly-written cache file is the right "fetched live, not stale" signal here. */
        private void AssertWikiCacheWasFreshlyFetched(DateTime testStartedUtc)
        {
            var cachePath = PluginPaths.CacheFilePath(WikiCacheDirName, WikiCacheFileName);
            Assert.True(File.Exists(cachePath), $"Expected the Luma wiki cache file at '{cachePath}' after a successful install.");

            var writtenAtUtc = File.GetLastWriteTimeUtc(cachePath);
            Assert.True(writtenAtUtc >= testStartedUtc.AddSeconds(-2),
                $"Luma wiki cache file at '{cachePath}' wasn't refreshed by this test run " +
                $"(last written {writtenAtUtc:u}, test started {testStartedUtc:u}) — install may " +
                "have reused stale cached data instead of fetching live.");
        }

        private static LumaRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, LumaRecordDto>>(File.ReadAllText(path));
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

        private class LumaRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public List<string> InstalledFiles { get; set; }
        }
    }
}
