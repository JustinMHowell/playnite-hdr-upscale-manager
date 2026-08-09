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
    /* Local-only integration test for the DXVK+Lilium HDR install/uninstall round trip — same
       shape as RenoDxInstallUiTests/LumaInstallUiTests, see RenoDxInstallUiTests' own doc comment
       for the reasoning that carries over unchanged (real UI buttons not direct service calls,
       per-Theory-case isolated Playnite instance).

       Two structural differences from RenoDX/Luma:

       - No wiki cache to assert freshness on. DxvkService.InstallAsync hits GitHub's releases
         API directly (see DxvkService.cs) rather than a crawled/cached "matched games" list.

       - The Install button can start out genuinely disabled, not just racing a WPF requery.
         Mafia's Game.exe import layout reads as GraphicsApi.Unknown on its own PE header — same
         class of gap confirmed live for The Witcher 3 earlier — so CanInstallDxvk only flips
         true once ManagerViewModel's async PCGamingWiki lookup resolves it, a real network
         round-trip. This test also caught DxvkService.InstallAsync ignoring that resolved value
         entirely and re-inspecting the raw PE header itself (fixed alongside this test — see
         KnownDxvkApiOrNull in ManagerViewModel.cs).

       Runs against Mafia (DirectX 8.1, resolved only via the PCGamingWiki fallback — confirmed
       live: its API table lists a single unambiguous "8.1") and Castle Crashers (DirectX 9,
       resolved directly from its own PE import table, no network wait needed) — see
       GAMES-TO-TEST.md's "DXVK+Lilium HDR picks" section. Both installed by the user specifically
       for this test. */
    [Collection(PlayniteUiCollection.Name)]
    public class DxvkInstallUiTests
    {
        private const string RecordStoreFileName = "dxvk_installs.json";

        private readonly ITestOutputHelper output;

        public DxvkInstallUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // Maps each [InlineData] game name to its HUM_UITEST_{KEY}_GAME_ID/_INSTALL_DIR key —
        // see PLAN.md §9's scenario table for the real values to set locally.
        private static string ConfigKeyFor(string gameName) => gameName switch
        {
            "Mafia" => "MAFIA",
            "Castle Crashers" => "CASTLECRASHERS",
            _ => throw new ArgumentException($"No LocalTestGameConfig key mapped for '{gameName}' — add one.", nameof(gameName))
        };

        [Theory]
        [InlineData("Mafia")]
        [InlineData("Castle Crashers")]
        public void InstallThenUninstallDxvk_FromManagerWindow_DeploysDllsAndCleansUpFully(string gameName)
        {
            // Fails fast (Directory.Exists check) if this game isn't actually installed right
            // now, instead of only surfacing that several UI-driven steps later.
            LocalTestGameConfig.Resolve(ConfigKeyFor(gameName));

            var preExisting = FindRecordByGameName(gameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{gameName}' already has a tracked DXVK+Lilium install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs " +
                    "then uninstalls DXVK+Lilium as part of its own run, so it refuses to run " +
                    "against a game that already has a real install it didn't create itself — " +
                    "uninstall DXVK+Lilium for this game manually first.");
            }

            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

            try
            {
                var record = ClickInstallUntilRecordAppears(managerWindow, gameName);

                Assert.NotEmpty(record.InstalledDlls);
                foreach (var dll in record.InstalledDlls)
                {
                    var fullPath = Path.Combine(record.InstallPath, dll);
                    Assert.True(File.Exists(fullPath),
                        $"DXVK+Lilium install wrote a record listing '{dll}' but it isn't on disk at '{fullPath}'.");
                }

                string confPath = null;
                if (record.DeployedConf)
                {
                    confPath = Path.Combine(record.InstallPath, "dxvk.conf");
                    Assert.True(File.Exists(confPath),
                        $"DXVK+Lilium install reported DeployedConf but 'dxvk.conf' isn't on disk at '{confPath}'.");
                }

                ClickUninstallUntilRecordGone(managerWindow, gameName);

                foreach (var dll in record.InstalledDlls)
                {
                    var fullPath = Path.Combine(record.InstallPath, dll);
                    var wasBackedUp = record.BackedUpFiles != null && record.BackedUpFiles.Contains(dll);

                    // A backed-up file gets restored (moved back), so it should exist again after
                    // uninstall; one that DXVK dropped fresh should be gone.
                    var reachedExpectedState = Retry.WhileFalse(
                        () => File.Exists(fullPath) == wasBackedUp,
                        timeout: TimeSpan.FromSeconds(15),
                        interval: TimeSpan.FromMilliseconds(250)).Result;

                    Assert.True(reachedExpectedState, wasBackedUp
                        ? $"Uninstall should have restored the original '{dll}' at '{fullPath}' but it's still missing."
                        : $"Uninstall removed the tracked record but left '{dll}' behind at '{fullPath}' — " +
                            "this game's install folder is no longer back to its pre-test state.");
                }

                if (confPath != null)
                {
                    var confGone = Retry.WhileFalse(
                        () => !File.Exists(confPath),
                        timeout: TimeSpan.FromSeconds(15),
                        interval: TimeSpan.FromMilliseconds(250)).Result;

                    Assert.True(confGone,
                        $"Uninstall removed the tracked record but left 'dxvk.conf' behind at '{confPath}'.");
                }
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

        /* Re-clicks Install every couple seconds until a record appears, covering both the
           ordinary WPF CommandManager requery race (see RenoDxInstallUiTests) and the slower,
           real wait for ManagerViewModel's async PCGamingWiki lookup to resolve the button's
           IsEnabled in the first place — a click landing while the button is still disabled is
           a silent no-op either way, so there's no need to tell the two cases apart here. */
        private DxvkRecordDto ClickInstallUntilRecordAppears(Window managerWindow, string gameName)
        {
            const int maxAttempts = 20;
            DxvkRecordDto record = null;

            for (var attempt = 1; attempt <= maxAttempts && record == null; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "DxvkInstall", output);
                Retry.WhileFalse(
                    () => (record = FindRecordByGameName(gameName)) != null,
                    timeout: TimeSpan.FromSeconds(2),
                    interval: TimeSpan.FromMilliseconds(250));
            }

            if (record == null)
            {
                output.WriteLine($"No DXVK+Lilium install record for '{gameName}' appeared after {maxAttempts} Install clicks.");
                output.WriteLine($"Record store checked: {PluginPaths.RecordStorePath(RecordStoreFileName)}");
                throw new InvalidOperationException(
                    "DXVK+Lilium install didn't complete — either the button stayed disabled the " +
                    "whole time (graphics API never resolved), the download failed, or the click " +
                    "never reached the command. Check the manager window's status text or the " +
                    "plugin log for the real reason.");
            }

            return record;
        }

        /* See RenoDxInstallUiTests.ClickUninstallUntilRecordGone for why a single click isn't
           reliable — same WPF CommandManager requery race, same fix. */
        private void ClickUninstallUntilRecordGone(Window managerWindow, string gameName)
        {
            const int maxAttempts = 5;
            var recordGone = false;

            for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "DxvkUninstall", output);
                recordGone = Retry.WhileFalse(
                    () => FindRecordByGameName(gameName) == null,
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
            }

            Assert.True(recordGone,
                $"DXVK+Lilium install record for '{gameName}' was still present after {maxAttempts} " +
                "Uninstall clicks.");
        }

        private static DxvkRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, DxvkRecordDto>>(File.ReadAllText(path));
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

        private class DxvkRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public List<string> InstalledDlls { get; set; }
            public List<string> BackedUpFiles { get; set; }
            public bool DeployedConf { get; set; }
        }
    }
}
