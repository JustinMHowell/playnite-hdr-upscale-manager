using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Gamepad counterpart to OptiScalerInstallUiTests — see FullscreenReShadeToggleGamepadUiTests
       for why a real virtual controller is used instead of a UIA Toggle() call. OptiScaler always
       needs a real DLSS-only-vs-has-FSR/XeSS choice (see ManagerFullscreenView.xaml's
       OptiScalerToggle comment), so there's no single "install" action a toggle could drive on
       its own — installing still goes through a gamepad press on the matching variant button
       (OptiScalerInstallNoDlss for Deep Rock Galactic, which ships FSR2 natively per
       GAMES-TO-TEST.md), and only the resulting "Installed" toggle (enabled once a record exists)
       is what a second gamepad A press uses to uninstall.

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenOptiScalerToggleGamepadUiTests
    {
        private const string GameName = "Deep Rock Galactic";
        private const string GameConfigKey = "DEEPROCKGALACTIC";
        private const string RecordStoreFileName = "optiscaler_installs.json";
        private const string InstallButtonAutomationId = "OptiScalerInstallNoDlss";

        private readonly ITestOutputHelper output;

        public FullscreenOptiScalerToggleGamepadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PressA_OnInstallButtonThenToggle_InstallsThenUninstalls_ARealOptiScalerDeploy()
        {
            LocalTestGameConfig.Resolve(GameConfigKey);

            var preExisting = FindRecordByGameName(GameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked OptiScaler install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs and " +
                    "uninstalls OptiScaler itself via the gamepad, so it refuses to run against a " +
                    "game with a real install it didn't create.");
            }

            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            var installButton = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId(InstallButtonAutomationId));
            Assert.True(installButton != null, $"'{InstallButtonAutomationId}' button not found in the Fullscreen manager window.");

            using var pad = new GamepadController();

            // GamepadFocus.PressUntil re-focuses and re-presses if the expected effect hasn't
            // landed yet — a gamepad A press activates whatever WPF's real focused element
            // currently is, and that can drift between presses even within the same test.
            OptiScalerRecordDto record = null;
            try
            {
                GamepadFocus.PressUntil(installButton, pad, output, InstallButtonAutomationId,
                    () => (record = FindRecordByGameName(GameName)) != null,
                    perAttemptTimeout: TimeSpan.FromSeconds(60));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnInstallButtonThenToggle_InstallsThenUninstalls_ARealOptiScalerDeploy), output);
                throw;
            }

            Assert.NotEmpty(record.InstalledFiles);

            var hadBackup = record.InstalledFiles.ToDictionary(
                relative => relative,
                relative => File.Exists(Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar)) + ".original"));

            foreach (var relative in record.InstalledFiles)
            {
                var fullPath = Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar));
                var isDirectory = relative.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
                Assert.True(isDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath),
                    $"OptiScaler install recorded '{relative}' but it isn't on disk at '{fullPath}'.");
            }

            var toggle = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("OptiScalerToggle"));
            Assert.True(toggle != null, "OptiScalerToggle checkbox not found in the Fullscreen manager window.");

            var togglePattern = toggle.Patterns.Toggle.Pattern;
            var toggleTurnedOn = Retry.WhileFalse(
                () => togglePattern.ToggleState.Value == ToggleState.On,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(250)).Result;
            Assert.True(toggleTurnedOn, "OptiScalerToggle didn't turn on after a successful install — IsOptiScalerInstalled/RefreshStatuses may not be wired correctly.");

            output.WriteLine($"OptiScaler installed via gamepad at '{record.InstallPath}' ({string.Join(", ", record.InstalledFiles)}).");

            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "OptiScalerToggle",
                    () => FindRecordByGameName(GameName) == null,
                    perAttemptTimeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnInstallButtonThenToggle_InstallsThenUninstalls_ARealOptiScalerDeploy), output);
                throw;
            }

            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);

            foreach (var relative in record.InstalledFiles)
            {
                var fullPath = Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar));
                var backupPath = fullPath + ".original";

                // A file that collided with one the game already shipped gets restored from its
                // ".original" backup on uninstall (see OptiScalerInstallUiTests); one OptiScaler
                // dropped fresh should simply be gone.
                var reachedExpectedState = Retry.WhileFalse(
                    () => hadBackup[relative]
                        ? File.Exists(fullPath) && !File.Exists(backupPath)
                        : !File.Exists(fullPath),
                    timeout: TimeSpan.FromSeconds(15),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                Assert.True(reachedExpectedState, hadBackup[relative]
                    ? $"Uninstall should have restored the original '{relative}' at '{fullPath}' but it's still missing or the backup wasn't consumed."
                    : $"Uninstall removed the record but left '{relative}' behind at '{fullPath}'.");
            }

            output.WriteLine("OptiScaler uninstalled via gamepad — game folder is back to its pre-test state.");
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
                // Same mid-write tolerance as FullscreenLumaToggleGamepadUiTests.
                return null;
            }
        }

        private class OptiScalerRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public List<string> InstalledFiles { get; set; }
        }
    }
}
