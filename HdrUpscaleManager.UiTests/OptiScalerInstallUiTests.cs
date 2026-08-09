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
    /* Local-only integration test for the OptiScaler install/uninstall round trip — same shape as
       DxvkInstallUiTests (real UI buttons, per-Theory-case isolated Playnite instance, no wiki
       cache to assert freshness on since OptiScalerService.InstallAsync hits GitHub's releases
       API directly). See RenoDxInstallUiTests' own doc comment for the reasoning that carries
       over unchanged.

       Runs all three OptiScaler scenarios from GAMES-TO-TEST.md's "OptiScaler picks" section,
       each via the manager-window button matching what the game actually ships:

       - Control (Ultimate Edition): DLSS-only, no native FSR ever shipped — "Install (DLSS-only
         game)" (dlssInputs: true).
       - Deep Rock Galactic: ships DLSS + FSR2 natively, never patched to FSR3/4 — "Install (has
         FSR/XeSS)" (dlssInputs: false), testing the FSR2->FSR4 override.
       - The Riftbreaker: ships FSR 1.0 natively, no FSR2/3 patch found — same button, testing the
         FSR1->FSR4 override.

       The resolved proxy DLL/OptiScaler.ini/OptiFG support files are usually brand new on a
       fresh install, but not always — confirmed live 2026-08-09 that The Riftbreaker's real
       deploy directory (bin\, next to its actual exe — see PeInspector.ResolveDeployDirectory)
       already ships its own libxess.dll, the same name OptiScaler's own bundle uses for Intel
       XeSS support. That exercises FileBackup's restore-on-uninstall path here too, not just in
       DxvkInstallUiTests (Mafia) — see hadBackup below for how this test tells the two cases
       apart when asserting the post-uninstall state. */
    [Collection(PlayniteUiCollection.Name)]
    public class OptiScalerInstallUiTests
    {
        private const string RecordStoreFileName = "optiscaler_installs.json";
        private const string InstallDlssOnlyButton = "OptiScalerInstallDlss";
        private const string InstallHasFsrButton = "OptiScalerInstallNoDlss";

        private readonly ITestOutputHelper output;

        public OptiScalerInstallUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // Maps each [InlineData] game name to its HUM_UITEST_{KEY}_GAME_ID/_INSTALL_DIR key —
        // see PLAN.md §9's scenario table for the real values to set locally.
        private static string ConfigKeyFor(string gameName) => gameName switch
        {
            "Control Ultimate Edition" => "CONTROL",
            "Deep Rock Galactic" => "DEEPROCKGALACTIC",
            "The Riftbreaker" => "RIFTBREAKER",
            _ => throw new ArgumentException($"No LocalTestGameConfig key mapped for '{gameName}' — add one.", nameof(gameName))
        };

        [Theory]
        [InlineData("Control Ultimate Edition", InstallDlssOnlyButton)]
        [InlineData("Deep Rock Galactic", InstallHasFsrButton)]
        [InlineData("The Riftbreaker", InstallHasFsrButton)]
        public void InstallThenUninstallOptiScaler_FromManagerWindow_DeploysFilesAndCleansUpFully(string gameName, string installButton)
        {
            // Fails fast (Directory.Exists check) if this game isn't actually installed right
            // now, instead of only surfacing that several UI-driven steps later.
            LocalTestGameConfig.Resolve(ConfigKeyFor(gameName));

            var preExisting = FindRecordByGameName(gameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{gameName}' already has a tracked OptiScaler install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs " +
                    "then uninstalls OptiScaler as part of its own run, so it refuses to run " +
                    "against a game that already has a real install it didn't create itself — " +
                    "uninstall OptiScaler for this game manually first.");
            }

            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

            try
            {
                var record = ClickInstallUntilRecordAppears(managerWindow, gameName, installButton);

                Assert.False(string.IsNullOrEmpty(record.DllName));
                Assert.NotEmpty(record.InstalledFiles);

                // A file OptiScaler bundles can collide with one the game already ships under
                // the same name at its real deploy directory (confirmed live 2026-08-09: The
                // Riftbreaker's own bin\libxess.dll collides with OptiScaler's bundled Intel XeSS
                // runtime of the same name) — FileBackup backs the original up as ".original"
                // and Uninstall restores it, so such a file is expected to still EXIST
                // afterward, not be gone. Snapshot which entries actually have a backup right
                // after install, before deciding what "cleaned up" means for each one below.
                var hadBackup = record.InstalledFiles.ToDictionary(
                    relative => relative,
                    relative => File.Exists(Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar)) + ".original"));

                foreach (var relative in record.InstalledFiles)
                {
                    AssertInstalledEntryExists(record.InstallPath, relative);
                }

                if (record.OptiPatcherInstalled)
                {
                    Assert.True(File.Exists(Path.Combine(record.InstallPath, "plugins", "OptiPatcher.asi")),
                        "Record says OptiPatcher was installed, but 'plugins\\OptiPatcher.asi' isn't on disk.");
                }

                if (record.DlssDllInstalled)
                {
                    Assert.True(File.Exists(Path.Combine(record.InstallPath, "nvngx_dlss.dll")),
                        "Record says the DLSS-input DLL was staged, but 'nvngx_dlss.dll' isn't on disk.");
                }

                ClickUninstallUntilRecordGone(managerWindow, gameName);

                foreach (var relative in record.InstalledFiles)
                {
                    if (hadBackup[relative])
                        AssertBackedUpEntryRestored(record.InstallPath, relative);
                    else
                        AssertInstalledEntryGone(record.InstallPath, relative);
                }

                if (record.OptiPatcherInstalled)
                {
                    Assert.False(File.Exists(Path.Combine(record.InstallPath, "plugins", "OptiPatcher.asi")),
                        "Uninstall removed the tracked record but left 'plugins\\OptiPatcher.asi' behind.");
                }

                if (record.DlssDllInstalled)
                {
                    Assert.False(File.Exists(Path.Combine(record.InstallPath, "nvngx_dlss.dll")),
                        "Uninstall removed the tracked record but left 'nvngx_dlss.dll' behind.");
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

        private static void AssertInstalledEntryExists(string installPath, string relative)
        {
            var isDirectory = relative.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            var fullPath = Path.Combine(installPath, relative.TrimEnd(Path.DirectorySeparatorChar));

            if (isDirectory)
            {
                Assert.True(Directory.Exists(fullPath),
                    $"OptiScaler install recorded directory '{relative}' but it isn't on disk at '{fullPath}'.");
            }
            else
            {
                Assert.True(File.Exists(fullPath),
                    $"OptiScaler install recorded '{relative}' but it isn't on disk at '{fullPath}'.");
            }
        }

        private void AssertInstalledEntryGone(string installPath, string relative)
        {
            var isDirectory = relative.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            var fullPath = Path.Combine(installPath, relative.TrimEnd(Path.DirectorySeparatorChar));

            var gone = Retry.WhileFalse(
                () => isDirectory ? !Directory.Exists(fullPath) : !File.Exists(fullPath),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(250)).Result;

            if (!gone)
                output.WriteLine($"'{fullPath}' still exists after uninstall (see screenshot for context if the manager window was captured elsewhere in this run).");

            Assert.True(gone,
                $"Uninstall removed the tracked record but left '{relative}' behind at '{fullPath}' — " +
                "this game's install folder is no longer back to its pre-test state.");
        }

        /* Counterpart to AssertInstalledEntryGone for an entry that collided with a file the
           game already shipped under the same name at its real deploy directory — Uninstall
           deletes OptiScaler's own copy and restores the ".original" backup, so the expected
           end state is "exists again", and the backup itself must be fully consumed. */
        private static void AssertBackedUpEntryRestored(string installPath, string relative)
        {
            var fullPath = Path.Combine(installPath, relative.TrimEnd(Path.DirectorySeparatorChar));
            var backupPath = fullPath + ".original";

            var restored = Retry.WhileFalse(
                () => File.Exists(fullPath) && !File.Exists(backupPath),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(250)).Result;

            Assert.True(restored,
                $"Uninstall should have restored the pre-existing '{relative}' from its '.original' backup at " +
                $"'{fullPath}' (exists: {File.Exists(fullPath)}, backup still present: {File.Exists(backupPath)}).");
        }

        /* Re-clicks Install every couple seconds until a record appears — the plain WPF
           CommandManager requery race (see RenoDxInstallUiTests), not the slower disabled-button
           wait DxvkInstallUiTests needs: OptiScalerInstallDlssCommand's CanExecute is just
           "!IsBusy", no async graphics-API resolution to wait on. */
        private OptiScalerRecordDto ClickInstallUntilRecordAppears(Window managerWindow, string gameName, string installButton)
        {
            const int maxAttempts = 10;
            OptiScalerRecordDto record = null;

            for (var attempt = 1; attempt <= maxAttempts && record == null; attempt++)
            {
                ManagerSectionNavigation.ClickButton(managerWindow, installButton, output);
                Retry.WhileFalse(
                    () => (record = FindRecordByGameName(gameName)) != null,
                    timeout: TimeSpan.FromSeconds(6),
                    interval: TimeSpan.FromMilliseconds(500));
            }

            if (record == null)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(ClickInstallUntilRecordAppears), output);
                output.WriteLine($"No OptiScaler install record for '{gameName}' appeared after {maxAttempts} Install clicks.");
                output.WriteLine($"Record store checked: {PluginPaths.RecordStorePath(RecordStoreFileName)}");
                throw new InvalidOperationException(
                    "OptiScaler install didn't complete — either the click never reached the " +
                    "command, the GitHub release download failed, or the game's executable " +
                    "couldn't be inspected. Check the manager window's status text or the plugin " +
                    "log for the real reason.");
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
                ManagerSectionNavigation.ClickButton(managerWindow, "OptiScalerUninstall", output);
                recordGone = Retry.WhileFalse(
                    () => FindRecordByGameName(gameName) == null,
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
            }

            if (!recordGone)
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(ClickUninstallUntilRecordGone), output);

            Assert.True(recordGone,
                $"OptiScaler install record for '{gameName}' was still present after {maxAttempts} " +
                "Uninstall clicks.");
        }

        private static OptiScalerRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, OptiScalerRecordDto>>(File.ReadAllText(path));
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
