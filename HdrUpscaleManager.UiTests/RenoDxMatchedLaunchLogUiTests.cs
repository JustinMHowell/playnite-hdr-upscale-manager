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
    /* Scenario 2 of PLAN.md §9 — real-launch log verification. Installs RenoDX (wiki-matched
       path), which auto-installs ReShade first if missing (EnsureReShadeInstalledForRenoDxAsync),
       then launches the real game and confirms via ReShade.log that ReShade actually loaded and
       scanned for addons — not just that files landed on disk, which RenoDxInstallUiTests already
       covers.

       Deliberately NOT asserting a specific "RenoDX addon recognized" log line yet — same
       calibration-first principle as scenario 1 (ReShadeOnlyLaunchLogUiTests): this run's job is
       to capture what ReShade's log looks like with a real RenoDX addon present, so a follow-up
       can pick the actual confirmed line to assert on.

       Game: Hollow Knight — GAMES-TO-TEST.md's own "safest bet" RenoDX-matched pick, and its real
       exe (hollow_knight.exe) sits at the install root, so no nested-exe complexity here (unlike
       Deep Rock Galactic in OptiScalerReShadeCoexistenceLaunchLogUiTests) — still searches
       recursively for safety/consistency rather than assuming that. */
    [Collection(PlayniteUiCollection.Name)]
    public class RenoDxMatchedLaunchLogUiTests
    {
        private const string GameName = "Hollow Knight";
        private const string GameConfigKey = "HOLLOWKNIGHT";
        private const string RenoDxRecordStoreFileName = "renodx_installs.json";

        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };

        private readonly ITestOutputHelper output;

        public RenoDxMatchedLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallRenoDxMatchedThenLaunch_RealGame_ReShadeLogShowsAddonScan()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var dir = game.InstallDirectory;
            GuardNoPreExistingInstall(dir);

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
            string resolvedDir = null;
            string addonPath = null;

            try
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "RenoDxInstall", output);

                RenoDxRecordDto record = null;
                var appeared = Retry.WhileFalse(
                    () => (record = FindRenoDxRecordByGameName(GameName)) != null,
                    timeout: TimeSpan.FromSeconds(60),
                    interval: TimeSpan.FromMilliseconds(500)).Result;

                if (!appeared)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallRenoDxMatchedThenLaunch_RealGame_ReShadeLogShowsAddonScan), output);
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }
                Assert.True(appeared, $"RenoDX install didn't complete for '{GameName}' within 60s.");

                addonPath = record.InstallPath;
                resolvedDir = Path.GetDirectoryName(addonPath);
                Assert.True(File.Exists(addonPath), $"RenoDX record exists but its addon file isn't on disk at '{addonPath}'.");
                output.WriteLine($"RenoDX installed as '{addonPath}'.");

                var reShadeProxyPath = FindExistingReShadeProxyRecursive(resolvedDir);
                Assert.True(reShadeProxyPath != null,
                    $"RenoDX needs ReShade as a host, but no ReShade proxy DLL was found under '{resolvedDir}' — " +
                    "EnsureReShadeInstalledForRenoDxAsync should have auto-installed it first.");
                output.WriteLine($"ReShade (auto-installed for RenoDX) found at '{reShadeProxyPath}'.");

                managerWindow.Close();
                managerWindow = null;

                var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                    game.GameId, resolvedDir, new[] { "ReShade.log" }, output);

                var reShadeLogPath = Path.Combine(resolvedDir, "ReShade.log");
                var content = logContents[reShadeLogPath];

                Assert.False(string.IsNullOrWhiteSpace(content), "ReShade.log is missing or empty — ReShade may not have loaded, so RenoDX couldn't either.");

                output.WriteLine("--- ReShade.log content (first 4000 chars) ---");
                output.WriteLine(content.Length > 4000 ? content.Substring(0, 4000) : content);
                output.WriteLine("--- end ReShade.log content ---");

                Assert.Contains("Searching for add-ons", content);
            }
            finally
            {
                CleanUp(fixture, managerWindow, dir, resolvedDir, addonPath, testStartedUtc);
            }
        }

        private void GuardNoPreExistingInstall(string dir)
        {
            var record = FindRenoDxRecordByGameName(GameName);
            var existingReShade = FindExistingReShadeProxyRecursive(dir);

            if (record != null || existingReShade != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a real RenoDX/ReShade install before this test started " +
                    $"(record={record != null}, reShadeProxy={existingReShade}). This test installs and cleans " +
                    "up both components itself — remove any leftover manually first.");
            }
        }

        private void CleanUp(PlayniteFixture fixture, Window managerWindow, string dir, string resolvedDir, string addonPath, DateTime testStartedUtc)
        {
            try
            {
                if (managerWindow == null)
                {
                    // Reuses the outer PlayniteFixture — see LumaMatchedLaunchLogUiTests.CleanUp
                    // for why constructing a second one here was a real bug, not a flake.
                    managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
                    CleanUpViaManagerWindow(managerWindow, dir, resolvedDir, addonPath, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
                else
                {
                    CleanUpViaManagerWindow(managerWindow, dir, resolvedDir, addonPath, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — '{GameName}' may need manual cleanup: {ex.Message}");
                throw;
            }
        }

        private void CleanUpViaManagerWindow(Window managerWindow, string dir, string resolvedDir, string addonPath, DateTime testStartedUtc)
        {
            if (FindRenoDxRecordByGameName(GameName) != null)
            {
                const int maxAttempts = 5;
                var recordGone = false;
                for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
                {
                    ManagerSectionNavigation.ClickButton(managerWindow, "RenoDxUninstall", output);
                    recordGone = Retry.WhileFalse(
                        () => FindRenoDxRecordByGameName(GameName) == null,
                        timeout: TimeSpan.FromSeconds(3),
                        interval: TimeSpan.FromMilliseconds(250)).Result;
                }
                if (!recordGone) output.WriteLine($"RenoDX record for '{GameName}' still present after {maxAttempts} Uninstall clicks.");
            }

            // ReShade has no Uninstall action of its own (see RenoDxInstallUiTests' own comment
            // on this) — CreationTimeUtc-guarded so a pre-existing ReShade install this test
            // didn't create is left alone.
            var cleanupDir = resolvedDir ?? dir;
            foreach (var candidate in ReShadeProxyDllCandidates)
            {
                CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, candidate), testStartedUtc);
            }
            CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, "ReShade.log"), testStartedUtc);
            CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, "ReShade.ini"), testStartedUtc);

            if (resolvedDir == null)
            {
                foreach (var candidate in ReShadeProxyDllCandidates.Append("ReShade.log").Append("ReShade.ini"))
                {
                    var found = FindFileRecursive(dir, candidate);
                    if (found != null) CleanUpFreshlyCreatedFile(found, testStartedUtc);
                }
            }

            // Belt-and-suspenders in case Uninstall's own click loop above failed but the file
            // still exists (e.g. window closed before uninstall could run).
            if (addonPath != null && File.Exists(addonPath))
            {
                try { File.Delete(addonPath); output.WriteLine($"Cleaned up leftover RenoDX addon at '{addonPath}'."); }
                catch (Exception ex) { output.WriteLine($"Failed to delete leftover RenoDX addon at '{addonPath}': {ex.Message}"); }
            }
        }

        private static RenoDxRecordDto FindRenoDxRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RenoDxRecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, RenoDxRecordDto>>(File.ReadAllText(path));
                return all?.Values.FirstOrDefault(r => string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string FindExistingReShadeProxyRecursive(string gameInstallDirectory) =>
            ReShadeProxyDllCandidates
                .Select(name => FindFileRecursive(gameInstallDirectory, name))
                .FirstOrDefault(p => p != null);

        private static string FindFileRecursive(string gameInstallDirectory, string fileName)
        {
            try
            {
                return Directory.EnumerateFiles(gameInstallDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void CleanUpFreshlyCreatedFile(string path, DateTime testStartedUtc)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (File.GetCreationTimeUtc(path) >= testStartedUtc.AddSeconds(-2))
            {
                File.Delete(path);
                output.WriteLine($"Cleaned up '{path}' (created by this test run).");
            }
            else
            {
                output.WriteLine($"Leaving '{path}' in place — it predates this test run.");
            }
        }

        private class RenoDxRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
        }
    }
}
