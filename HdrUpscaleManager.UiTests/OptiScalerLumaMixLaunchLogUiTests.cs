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
    /* Scenario 6 of PLAN.md §9 — OptiScaler + Luma mix. Unlike every other scenario in this
       pass, this one is EXPLORATORY, not a pass/fail correctness check: PLAN.md §6.5 already
       flags OptiScaler+Luma proxy-slot contention as a real, plausible, but never confirmed risk
       — Luma ships its own self-hosted dxgi.dll (confirmed a literal ReShade fork by
       LumaMatchedLaunchLogUiTests), and OptiScaler's own coexistence logic
       (OptiScalerService.ShouldDisplaceReShade) only recognizes ReShade's own drop-in install,
       not Luma's. This test's job is to observe and document actual current behavior, not assert
       a specific "works" outcome — a failure to chain-load Luma here is a finding to record in
       PLAN.md afterward, not a bug in this test.

       Game: Vanquish — same Luma-matched pick as LumaMatchedLaunchLogUiTests, reused per PLAN.md
       §9's own scenario table (both # 4 and #6 target the same game). */
    [Collection(PlayniteUiCollection.Name)]
    public class OptiScalerLumaMixLaunchLogUiTests
    {
        private const string GameName = "Vanquish";
        private const string GameConfigKey = "VANQUISH";
        private const string LumaRecordStoreFileName = "luma_installs.json";
        private const string OptiScalerRecordStoreFileName = "optiscaler_installs.json";
        private const string OptiScalerInstallButton = "Install (has FSR/XeSS)";

        private readonly ITestOutputHelper output;

        public OptiScalerLumaMixLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallLumaThenOptiScaler_RealGame_DocumentsActualCoexistenceBehavior()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var dir = game.InstallDirectory;
            GuardNoPreExistingInstall();

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
            LumaRecordDto lumaRecord = null;
            OptiScalerRecordDto optiScalerRecord = null;

            try
            {
                const int lumaAttempts = 10;
                var lumaAppeared = false;
                for (var attempt = 1; attempt <= lumaAttempts && !lumaAppeared; attempt++)
                {
                    ManagerSectionNavigation.ClickSectionButton(managerWindow, "Luma", "Install", output);
                    lumaAppeared = Retry.WhileFalse(
                        () => (lumaRecord = FindLumaRecordByGameName(GameName)) != null,
                        timeout: TimeSpan.FromSeconds(6),
                        interval: TimeSpan.FromMilliseconds(500)).Result;
                }
                Assert.True(lumaAppeared, $"Luma install didn't complete for '{GameName}' — can't observe the mix without it installed first.");
                output.WriteLine($"Luma installed under '{lumaRecord.InstallPath}'.");

                const int osAttempts = 10;
                var osAppeared = false;
                for (var attempt = 1; attempt <= osAttempts && !osAppeared; attempt++)
                {
                    ManagerSectionNavigation.ClickSectionButton(managerWindow, "OptiScaler (AMD)", OptiScalerInstallButton, output);
                    osAppeared = Retry.WhileFalse(
                        () => (optiScalerRecord = FindOptiScalerRecordByGameName(GameName)) != null,
                        timeout: TimeSpan.FromSeconds(6),
                        interval: TimeSpan.FromMilliseconds(500)).Result;
                }
                Assert.True(osAppeared, $"OptiScaler install didn't complete for '{GameName}'.");
                output.WriteLine($"OptiScaler installed as '{optiScalerRecord.DllName}' under '{optiScalerRecord.InstallPath}'.");

                // Observation, not assertion: does OptiScaler's own backup mechanism treat
                // Luma's dxgi.dll as "the game's original file" (since ShouldDisplaceReShade only
                // recognizes ReShade's own drop-in, not Luma's self-hosted one)?
                var lumaDxgiOriginal = Path.Combine(optiScalerRecord.InstallPath, "dxgi.dll.original");
                var backedUpLumaAsOriginal = File.Exists(lumaDxgiOriginal);
                output.WriteLine($"Luma's dxgi.dll backed up as 'dxgi.dll.original' (i.e. treated as the game's own file, not recognized as a coexistence peer): {backedUpLumaAsOriginal}");

                managerWindow.Close();
                managerWindow = null;

                var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                    game.GameId, optiScalerRecord.InstallPath, new[] { "OptiScaler.log", "ReShade.log" }, output);

                foreach (var kvp in logContents)
                {
                    output.WriteLine($"--- {kvp.Key} (exists: {kvp.Value != null}) ---");
                    if (kvp.Value == null) continue;
                    var content = kvp.Value;
                    output.WriteLine(content.Length > 3000 ? content.Substring(0, 3000) : content);
                    output.WriteLine("--- end ---");
                }

                // Deliberately not asserted — this whole scenario documents current behavior
                // rather than requiring a specific outcome (see class doc comment). Whether
                // OptiScaler itself loaded, whether it mentions Luma, and whether Luma's own
                // ReShade-formatted log still appears are all FINDINGS to record in PLAN.md, not
                // pass/fail conditions.
                var optiScalerLog = logContents[Path.Combine(optiScalerRecord.InstallPath, "OptiScaler.log")];
                var reShadeLog = logContents[Path.Combine(optiScalerRecord.InstallPath, "ReShade.log")];
                var optiScalerLoaded = !string.IsNullOrWhiteSpace(optiScalerLog);
                var mentionsLuma = optiScalerLoaded && optiScalerLog.IndexOf("luma", StringComparison.OrdinalIgnoreCase) >= 0;

                output.WriteLine($"FINDING: OptiScaler itself loaded (OptiScaler.log non-empty): {optiScalerLoaded}.");
                output.WriteLine($"FINDING: OptiScaler.log mentions Luma: {mentionsLuma}.");
                output.WriteLine($"FINDING: Luma's own ReShade-formatted log still present after OptiScaler backed up its dxgi.dll: {!string.IsNullOrWhiteSpace(reShadeLog)}.");
            }
            finally
            {
                CleanUp(managerWindow, dir, optiScalerRecord, lumaRecord, testStartedUtc);
            }
        }

        private void GuardNoPreExistingInstall()
        {
            var lumaRecord = FindLumaRecordByGameName(GameName);
            var optiScalerRecord = FindOptiScalerRecordByGameName(GameName);

            if (lumaRecord != null || optiScalerRecord != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a real Luma/OptiScaler install before this test started " +
                    $"(luma={lumaRecord != null}, optiScaler={optiScalerRecord != null}). This test installs " +
                    "and cleans up both components itself — remove any leftover manually first.");
            }
        }

        private void CleanUp(Window managerWindow, string dir, OptiScalerRecordDto optiScalerRecord, LumaRecordDto lumaRecord, DateTime testStartedUtc)
        {
            try
            {
                if (managerWindow == null)
                {
                    using var fixture = new PlayniteFixture();
                    fixture.GetMainWindow();
                    managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
                    CleanUpViaManagerWindow(managerWindow, optiScalerRecord, lumaRecord, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
                else
                {
                    CleanUpViaManagerWindow(managerWindow, optiScalerRecord, lumaRecord, testStartedUtc);
                    try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — '{GameName}' may need manual cleanup: {ex.Message}");
                throw;
            }
        }

        private void CleanUpViaManagerWindow(Window managerWindow, OptiScalerRecordDto optiScalerRecord, LumaRecordDto lumaRecord, DateTime testStartedUtc)
        {
            if (FindOptiScalerRecordByGameName(GameName) != null)
            {
                const int maxAttempts = 5;
                var recordGone = false;
                for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
                {
                    ManagerSectionNavigation.ClickSectionButton(managerWindow, "OptiScaler (AMD)", "Uninstall", output);
                    recordGone = Retry.WhileFalse(
                        () => FindOptiScalerRecordByGameName(GameName) == null,
                        timeout: TimeSpan.FromSeconds(3),
                        interval: TimeSpan.FromMilliseconds(250)).Result;
                }
                if (!recordGone) output.WriteLine($"OptiScaler record for '{GameName}' still present after {maxAttempts} Uninstall clicks.");
            }

            if (lumaRecord != null)
            {
                foreach (var relative in lumaRecord.InstalledFiles)
                {
                    CleanUpFreshlyCreatedFile(Path.Combine(lumaRecord.InstallPath, relative), testStartedUtc);
                }
                RemoveLumaRecord();
            }

            // Belt-and-suspenders: OptiScaler's own uninstall restores dxgi.dll.original if it
            // made one, but confirm and clean up anything left over from the exploratory checks.
            if (optiScalerRecord != null)
            {
                var leftoverOriginal = Path.Combine(optiScalerRecord.InstallPath, "dxgi.dll.original");
                CleanUpFreshlyCreatedFile(leftoverOriginal, testStartedUtc);
                CleanUpFreshlyCreatedFile(Path.Combine(optiScalerRecord.InstallPath, "OptiScaler.log"), testStartedUtc);
                CleanUpFreshlyCreatedFile(Path.Combine(optiScalerRecord.InstallPath, "ReShade.log"), testStartedUtc);
            }
        }

        private void RemoveLumaRecord()
        {
            try
            {
                var path = PluginPaths.RecordStorePath(LumaRecordStoreFileName);
                if (!File.Exists(path)) return;

                var all = JsonConvert.DeserializeObject<Dictionary<string, LumaRecordDto>>(File.ReadAllText(path)) ?? new Dictionary<string, LumaRecordDto>();
                var key = all.FirstOrDefault(kvp => string.Equals(kvp.Value.GameName, GameName, StringComparison.OrdinalIgnoreCase)).Key;
                if (key == null) return;

                all.Remove(key);
                File.WriteAllText(path, JsonConvert.SerializeObject(all));
                output.WriteLine($"Removed Luma record for '{GameName}' from '{path}'.");
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to remove Luma record for '{GameName}': {ex.Message}");
            }
        }

        private static LumaRecordDto FindLumaRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(LumaRecordStoreFileName);
            if (!File.Exists(path)) return null;
            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, LumaRecordDto>>(File.ReadAllText(path));
                return all?.Values.FirstOrDefault(r => string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
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
            catch { return null; }
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

        private class LumaRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public List<string> InstalledFiles { get; set; } = new List<string>();
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
