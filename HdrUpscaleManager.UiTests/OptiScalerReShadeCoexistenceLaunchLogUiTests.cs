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
    /* Scenario 8 of PLAN.md §9 — the real-launch pass for this session's OptiScaler/ReShade
       coexistence fix (OptiScalerService.ShouldDisplaceReShade/MoveReShadeToCoexistName/
       RestoreReShadeFromCoexistName, ReShadeInstallService.ResolveTargetDllName). The unit tests
       in OptiScalerReShadeCoexistenceTests.cs already prove the file-rename logic converges
       correctly on disk regardless of install order — this proves the *runtime* half: that
       OptiScaler's LoadReshade=true setting actually chain-loads ReShade once it's sitting at
       ReShadeCoexistName, not just that the file landed there.

       Both install orders are covered because the unit tests proved they converge to the same
       on-disk state, but only a real launch can confirm OptiScaler's own runtime behavior is the
       same regardless of which order produced that state.

       Deliberately searches for files RECURSIVELY under the game's install directory rather than
       only at its root — confirmed live 2026-08-09 on this very game that OptiScaler/ReShade
       deploy next to the real running exe (PeInspector.ResolveDeployDirectory), which can be a
       subfolder (Deep Rock Galactic's real exe is FSD\Binaries\Win64\FSD-Win64-Shipping.exe, not
       the root-level FSD.exe launcher stub). This test doesn't reference the main addon assembly
       (see README.md), so it can't call that resolver directly — recursive search is the
       addon-independent equivalent, and OptiScalerRecordDto.InstallPath (already tracked by the
       real addon) is used as the authoritative resolved directory once available.

       Game: Deep Rock Galactic — NOT The Riftbreaker despite PLAN.md §9 originally proposing it:
       confirmed live 2026-08-09 that The Riftbreaker's graphics API resolves to Unknown both
       locally (PE inspection) and via the PCGamingWiki fallback (its engine, "Schmetterling", IS
       recognized, just not with a confident API answer) — ReShade's own install genuinely can't
       drop in for it at all, a real limitation, not a bug in this session's coexistence fix.
       OptiScaler's own install still "works" there only because ResolveDllName defaults
       permissively to dxgi.dll for any non-Vulkan API including Unknown, masking the same gap. */
    [Collection(PlayniteUiCollection.Name)]
    public class OptiScalerReShadeCoexistenceLaunchLogUiTests
    {
        private const string GameName = "Deep Rock Galactic";
        private const string GameConfigKey = "DEEPROCKGALACTIC";
        private const string OptiScalerInstallButton = "OptiScalerInstallNoDlss";
        private const string OptiScalerRecordStoreFileName = "optiscaler_installs.json";

        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };
        private const string ReShadeCoexistName = "ReShade64.dll"; // OptiScalerService.ReShadeCoexistName

        private readonly ITestOutputHelper output;

        public OptiScalerReShadeCoexistenceLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallOptiScalerThenReShade_RealGame_BothLoadCorrectly()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var dir = game.InstallDirectory;
            GuardNoPreExistingInstall(dir);

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
            string resolvedDir = null;

            try
            {
                var record = ClickOptiScalerInstallUntilRecordAppears(managerWindow);
                resolvedDir = record.InstallPath;
                output.WriteLine($"OptiScaler installed as '{record.DllName}' under '{resolvedDir}'.");
                Assert.True(File.Exists(Path.Combine(resolvedDir, record.DllName)));

                var reShadeCoexistPath = Path.Combine(resolvedDir, ReShadeCoexistName);
                var landedAtCoexistName = ClickReShadeInstallUntilFileAppears(managerWindow, reShadeCoexistPath);

                if (!landedAtCoexistName)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallOptiScalerThenReShade_RealGame_BothLoadCorrectly), output);
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }

                Assert.True(landedAtCoexistName,
                    $"ReShade should have deployed straight to '{ReShadeCoexistName}' under '{resolvedDir}' since " +
                    $"OptiScaler was already installed for '{GameName}' — it never appeared within 30s. Check " +
                    "ReShadeInstallService.ResolveTargetDllName / OptiScalerService.IsInstalled.");

                RunLaunchAndAssertBothLogsLoaded(ref managerWindow, game, resolvedDir);
            }
            finally
            {
                CleanUpOptiScalerThenReShade(fixture, managerWindow, dir, resolvedDir, testStartedUtc);
            }
        }

        [Fact]
        public void InstallReShadeThenOptiScaler_RealGame_BothLoadCorrectly()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var dir = game.InstallDirectory;
            GuardNoPreExistingInstall(dir);

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
            string resolvedDir = null;

            try
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "ReShadeInstall", output);

                string reShadeInitialPath = null;
                var reShadeLanded = Retry.WhileFalse(
                    () => (reShadeInitialPath = FindExistingReShadeProxyRecursive(dir)) != null,
                    timeout: TimeSpan.FromSeconds(30),
                    interval: TimeSpan.FromMilliseconds(500)).Result;
                if (!reShadeLanded)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallReShadeThenOptiScaler_RealGame_BothLoadCorrectly), output);
                    output.WriteLine("--- automation tree dump: manager window (ReShade install never landed) ---");
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }
                Assert.True(reShadeLanded, $"ReShade never appeared anywhere under '{dir}'.");
                resolvedDir = Path.GetDirectoryName(reShadeInitialPath);
                output.WriteLine($"ReShade installed as '{reShadeInitialPath}' (before OptiScaler existed).");

                var record = ClickOptiScalerInstallUntilRecordAppears(managerWindow);
                output.WriteLine($"OptiScaler installed as '{record.DllName}' under '{record.InstallPath}'.");

                Assert.Equal(resolvedDir, record.InstallPath,
                    ignoreCase: true);
                // OptiScaler's own resolution should agree with where ReShade actually landed —
                // both independently resolve the same real exe from the same install root.
                resolvedDir = record.InstallPath;

                var reShadeCoexistPath = Path.Combine(resolvedDir, ReShadeCoexistName);
                var movedToCoexistName = Retry.WhileFalse(
                    () => File.Exists(reShadeCoexistPath),
                    timeout: TimeSpan.FromSeconds(10),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                if (!movedToCoexistName)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallReShadeThenOptiScaler_RealGame_BothLoadCorrectly), output);
                }

                Assert.True(movedToCoexistName,
                    $"OptiScaler installed after ReShade already occupied '{Path.GetFileName(reShadeInitialPath)}' — " +
                    $"ReShade should have been moved to '{ReShadeCoexistName}' but wasn't. Check " +
                    "OptiScalerService.ShouldDisplaceReShade/MoveReShadeToCoexistName.");
                Assert.True(File.Exists(Path.Combine(resolvedDir, record.DllName)),
                    $"OptiScaler's own resolved dll name '{record.DllName}' isn't on disk after displacing ReShade.");

                RunLaunchAndAssertBothLogsLoaded(ref managerWindow, game, resolvedDir);
            }
            finally
            {
                CleanUpOptiScalerThenReShade(fixture, managerWindow, dir, resolvedDir, testStartedUtc);
            }
        }

        private void RunLaunchAndAssertBothLogsLoaded(
            ref Window managerWindow, LocalTestGameConfig.GameConfig game, string resolvedDir)
        {
            managerWindow.Close();
            managerWindow = null;

            var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                game.GameId, resolvedDir, new[] { "OptiScaler.log", "ReShade.log" }, output);

            foreach (var kvp in logContents)
            {
                output.WriteLine($"--- {kvp.Key} (first 3000 chars) ---");
                var content = kvp.Value ?? "(missing)";
                output.WriteLine(content.Length > 3000 ? content.Substring(0, 3000) : content);
                output.WriteLine("--- end ---");
            }

            var optiScalerLog = logContents[Path.Combine(resolvedDir, "OptiScaler.log")];
            var reShadeLog = logContents[Path.Combine(resolvedDir, "ReShade.log")];

            Assert.False(string.IsNullOrWhiteSpace(optiScalerLog), "OptiScaler.log is missing or empty — OptiScaler may not have loaded.");
            Assert.False(string.IsNullOrWhiteSpace(reShadeLog), "ReShade.log is missing or empty — ReShade may not have chain-loaded via OptiScaler's LoadReshade setting.");
        }

        private void GuardNoPreExistingInstall(string dir)
        {
            var record = FindOptiScalerRecordByGameName(GameName);
            var existingReShade = FindExistingReShadeProxyRecursive(dir);
            var existingCoexist = FindFileRecursive(dir, ReShadeCoexistName);
            var existingIni = FindFileRecursive(dir, "OptiScaler.ini");

            if (record != null || existingReShade != null || existingCoexist != null || existingIni != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a real OptiScaler/ReShade install before this test started " +
                    $"(record={record != null}, reShadeProxy={existingReShade}, coexist={existingCoexist}, " +
                    $"ini={existingIni}). This test installs and cleans up both components itself — remove any " +
                    "leftover manually first.");
            }
        }

        private void CleanUpOptiScalerThenReShade(PlayniteFixture fixture, Window managerWindow, string dir, string resolvedDir, DateTime testStartedUtc)
        {
            try
            {
                if (managerWindow == null)
                {
                    // The window was closed before launching the game — need it open again to
                    // click Uninstall. Reuses the outer PlayniteFixture rather than constructing a
                    // new one — see LumaMatchedLaunchLogUiTests.CleanUp for why a second one here
                    // was a real bug, not a flake.
                    managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
                    CleanUpViaManagerWindow(managerWindow, dir, resolvedDir, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
                else
                {
                    CleanUpViaManagerWindow(managerWindow, dir, resolvedDir, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — '{GameName}' may need manual cleanup: {ex.Message}");
                throw;
            }
        }

        private void CleanUpViaManagerWindow(Window managerWindow, string dir, string resolvedDir, DateTime testStartedUtc)
        {
            if (FindOptiScalerRecordByGameName(GameName) != null)
            {
                ClickOptiScalerUninstallUntilRecordGone(managerWindow);
            }

            // RestoreReShadeFromCoexistName (run by OptiScaler's own Uninstall) should have moved
            // ReShade64.dll back to its normal proxy name by now — ReShade has no Uninstall
            // action of its own, so whatever's left of it is this test's to remove directly.
            // Falls back to searching the whole install tree if resolvedDir was never determined
            // (e.g. an early failure before either component finished installing).
            var cleanupDir = resolvedDir ?? dir;
            foreach (var candidate in ReShadeProxyDllCandidates.Append(ReShadeCoexistName))
            {
                CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, candidate), testStartedUtc);
            }
            // Neither log is tracked in InstalledFiles (OptiScaler.ini is, but the .log files
            // aren't — see OptiScalerService.DeployIni) so OptiScaler's own Uninstall never
            // touches them; this test's job to remove.
            CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, "OptiScaler.log"), testStartedUtc);
            CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, "ReShade.log"), testStartedUtc);
            CleanUpFreshlyCreatedFile(Path.Combine(cleanupDir, "ReShade.ini"), testStartedUtc);

            if (resolvedDir == null)
            {
                // Never resolved a directory (failed before any install completed) — do one more
                // recursive sweep so a partial run doesn't leave anything behind anywhere in the tree.
                foreach (var candidate in ReShadeProxyDllCandidates.Append(ReShadeCoexistName).Append("OptiScaler.ini").Append("OptiScaler.log").Append("ReShade.log").Append("ReShade.ini"))
                {
                    var found = FindFileRecursive(dir, candidate);
                    if (found != null) CleanUpFreshlyCreatedFile(found, testStartedUtc);
                }
            }
        }

        /* Same WPF CommandManager re-query race ClickOptiScalerInstallUntilRecordAppears/
           RenoDxInstallUiTests already guard against — a single click landing before CanExecute
           re-queries after IsBusy flips back is a silent no-op, not a real install failure. */
        private bool ClickReShadeInstallUntilFileAppears(Window managerWindow, string expectedFilePath)
        {
            const int maxAttempts = 10;
            var appeared = false;

            for (var attempt = 1; attempt <= maxAttempts && !appeared; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "ReShadeInstall", output);
                appeared = Retry.WhileFalse(
                    () => File.Exists(expectedFilePath),
                    timeout: TimeSpan.FromSeconds(6),
                    interval: TimeSpan.FromMilliseconds(500)).Result;
            }

            return appeared;
        }

        private OptiScalerRecordDto ClickOptiScalerInstallUntilRecordAppears(Window managerWindow)
        {
            const int maxAttempts = 10;
            OptiScalerRecordDto record = null;

            for (var attempt = 1; attempt <= maxAttempts && record == null; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, OptiScalerInstallButton, output);
                Retry.WhileFalse(
                    () => (record = FindOptiScalerRecordByGameName(GameName)) != null,
                    timeout: TimeSpan.FromSeconds(6),
                    interval: TimeSpan.FromMilliseconds(500));
            }

            if (record == null)
            {
                throw new InvalidOperationException(
                    $"OptiScaler install didn't complete for '{GameName}' after {maxAttempts} Install clicks. " +
                    $"Record store checked: {PluginPaths.RecordStorePath(OptiScalerRecordStoreFileName)}");
            }

            return record;
        }

        private void ClickOptiScalerUninstallUntilRecordGone(Window managerWindow)
        {
            const int maxAttempts = 5;
            var recordGone = false;

            for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "OptiScalerUninstall", output);
                recordGone = Retry.WhileFalse(
                    () => FindOptiScalerRecordByGameName(GameName) == null,
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
            }

            Assert.True(recordGone, $"OptiScaler install record for '{GameName}' was still present after {maxAttempts} Uninstall clicks.");
        }

        private static OptiScalerRecordDto FindOptiScalerRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(OptiScalerRecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, OptiScalerRecordDto>>(File.ReadAllText(path));
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

        /* Searches the whole install tree, not just the top level — see class doc comment for
           why (OptiScaler/ReShade deploy next to the real exe, which can be several folders
           deep). Cheap enough for a single Steam game's file count; not meant to scale beyond that. */
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

        private class OptiScalerRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public string DllName { get; set; }
            public List<string> InstalledFiles { get; set; }
            public bool OptiPatcherInstalled { get; set; }
            public bool DlssDllInstalled { get; set; }
        }
    }
}
