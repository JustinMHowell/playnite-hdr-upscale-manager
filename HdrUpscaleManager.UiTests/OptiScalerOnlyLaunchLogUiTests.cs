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
    /* Scenario 5 of PLAN.md §9 — real-launch log verification, OptiScaler alone (no ReShade/
       RenoDX/Luma layered on — see OptiScalerReShadeCoexistenceLaunchLogUiTests and
       OptiScalerRenoDxMixLaunchLogUiTests for those mixes, and OptiScalerLumaMixLaunchLogUiTests
       for the known-broken one). Confirms LogToFile=true (forced in OptiScalerService.DeployIni)
       actually produces a real, non-empty OptiScaler.log at runtime — OptiScalerInstallUiTests
       only proves the files/ini land on disk, not that OptiScaler's own DLL initializes and
       writes anything once the game is actually running.

       Asserts on "OptiScaler working as dxgi.dll" — confirmed live 2026-08-10 against a real
       OptiScaler.log (v0.9.4-final) — rather than the version banner line, since that changes
       with every OptiScaler release while this "it's actually acting as the proxy" line doesn't.

       Game: Deep Rock Galactic — ships DLSS + FSR2 natively (never patched to FSR3/4), so
       "Install (has FSR/XeSS)" exercises the FSR2->FSR4 override path; already the proven pick
       for every other OptiScaler scenario in this suite (install, toggle, RenoDX mix). Its real
       exe lives under FSD\Binaries\Win64\, not the install root — OptiScaler.log lands there too,
       hence resolving the deploy directory from the install record rather than assuming it's the
       install root (see OptiScalerRenoDxMixLaunchLogUiTests for the same reasoning). */
    [Collection(PlayniteUiCollection.Name)]
    public class OptiScalerOnlyLaunchLogUiTests
    {
        private const string GameName = "Deep Rock Galactic";
        private const string GameConfigKey = "DEEPROCKGALACTIC";
        private const string InstallButton = "OptiScalerInstallNoDlss";
        private const string RecordStoreFileName = "optiscaler_installs.json";

        private readonly ITestOutputHelper output;

        public OptiScalerOnlyLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallOptiScalerThenLaunch_RealGame_OptiScalerLogAppearsAndLoads()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var gameInstallDirectory = game.InstallDirectory;

            var preExisting = FindRecordByGameName(GameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked OptiScaler install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs and " +
                    "uninstalls OptiScaler itself, so it refuses to run against a game with a real " +
                    "install it didn't create — remove it manually first.");
            }

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

            OptiScalerRecordDto record = null;
            try
            {
                record = ClickInstallUntilRecordAppears(managerWindow);
                output.WriteLine($"OptiScaler installed as '{record.DllName}' under '{record.InstallPath}'.");

                foreach (var relative in record.InstalledFiles)
                {
                    var fullPath = Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar));
                    var isDirectory = relative.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
                    Assert.True(isDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath),
                        $"OptiScaler install recorded '{relative}' but it isn't on disk at '{fullPath}'.");
                }

                managerWindow.Close();
                managerWindow = null;

                var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                    game.GameId, record.InstallPath, new[] { "OptiScaler.log" }, output);

                var optiScalerLogPath = Path.Combine(record.InstallPath, "OptiScaler.log");
                var content = logContents[optiScalerLogPath];

                Assert.True(File.Exists(optiScalerLogPath), $"Expected 'OptiScaler.log' to exist at '{optiScalerLogPath}' after launching the game.");
                Assert.Contains("OptiScaler working as dxgi.dll", content);

                output.WriteLine("--- OptiScaler.log content (first 4000 chars) ---");
                output.WriteLine(content.Length > 4000 ? content.Substring(0, 4000) : content);
                output.WriteLine("--- end OptiScaler.log content ---");
            }
            finally
            {
                CleanUp(fixture, managerWindow, record, testStartedUtc);
            }
        }

        private OptiScalerRecordDto ClickInstallUntilRecordAppears(Window managerWindow)
        {
            const int maxAttempts = 10;
            OptiScalerRecordDto record = null;

            for (var attempt = 1; attempt <= maxAttempts && record == null; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, InstallButton, output);
                Retry.WhileFalse(
                    () => (record = FindRecordByGameName(GameName)) != null,
                    timeout: TimeSpan.FromSeconds(6),
                    interval: TimeSpan.FromMilliseconds(500));
            }

            if (record == null)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallOptiScalerThenLaunch_RealGame_OptiScalerLogAppearsAndLoads), output);
                throw new InvalidOperationException(
                    $"OptiScaler install didn't complete for '{GameName}' after {maxAttempts} Install clicks. " +
                    $"Record store checked: {PluginPaths.RecordStorePath(RecordStoreFileName)}");
            }

            return record;
        }

        private void CleanUp(PlayniteFixture fixture, Window managerWindow, OptiScalerRecordDto record, DateTime testStartedUtc)
        {
            try
            {
                // Reuses the outer PlayniteFixture rather than constructing a second one — see
                // LumaMatchedLaunchLogUiTests.CleanUp for why a second fixture here was a real bug.
                if (managerWindow == null) managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

                if (FindRecordByGameName(GameName) != null)
                {
                    const int maxAttempts = 5;
                    var recordGone = false;
                    for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
                    {
                        ManagerSectionNavigation.ClickButton(managerWindow, "OptiScalerUninstall", output);
                        recordGone = Retry.WhileFalse(
                            () => FindRecordByGameName(GameName) == null,
                            timeout: TimeSpan.FromSeconds(3),
                            interval: TimeSpan.FromMilliseconds(250)).Result;
                    }
                    if (!recordGone) output.WriteLine($"OptiScaler record for '{GameName}' still present after {maxAttempts} Uninstall clicks.");
                }

                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }

                // OptiScaler.log/OptiScaler.ini aren't part of Uninstall's tracked InstalledFiles
                // (see OptiScalerReShadeCoexistenceLaunchLogUiTests for the same cleanup gap) —
                // this run's own log/ini are safe to remove outright by creation time.
                if (record?.InstallPath != null)
                {
                    CleanUpFreshlyCreatedFile(Path.Combine(record.InstallPath, "OptiScaler.log"), testStartedUtc);
                    CleanUpFreshlyCreatedFile(Path.Combine(record.InstallPath, "OptiScaler.ini"), testStartedUtc);
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — '{GameName}' may need manual cleanup: {ex.Message}");
                throw;
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

        private static OptiScalerRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
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
