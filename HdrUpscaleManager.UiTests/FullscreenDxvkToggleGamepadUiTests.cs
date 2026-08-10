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
    /* Gamepad counterpart to DxvkInstallUiTests — see FullscreenReShadeToggleGamepadUiTests for
       why a real virtual controller is used instead of a UIA Toggle() call. Detection here goes
       through the DXVK install record (dxvk_installs.json), same reasoning as
       FullscreenLumaToggleGamepadUiTests.

       Castle Crashers specifically (not Mafia, DxvkInstallUiTests' other case): its DirectX 9 API
       resolves directly from its own PE import table with no PCGamingWiki network round-trip, so
       CanToggleDxvk is already true by the time the gamepad press lands — no extra wait needed to
       avoid racing the toggle's own enabled state.

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenDxvkToggleGamepadUiTests
    {
        private const string GameName = "Castle Crashers";
        private const string GameConfigKey = "CASTLECRASHERS";
        private const string RecordStoreFileName = "dxvk_installs.json";

        private readonly ITestOutputHelper output;

        public FullscreenDxvkToggleGamepadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PressA_OnDxvkToggle_InstallsThenUninstalls_ARealDxvkDrop()
        {
            LocalTestGameConfig.Resolve(GameConfigKey);

            var preExisting = FindRecordByGameName(GameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked DXVK+Lilium install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs and " +
                    "uninstalls DXVK+Lilium itself via the toggle, so it refuses to run against a " +
                    "game with a real install it didn't create.");
            }

            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            var toggle = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("DxvkToggle"));
            Assert.True(toggle != null, "DxvkToggle checkbox not found in the Fullscreen manager window.");

            var togglePattern = toggle.Patterns.Toggle.Pattern;
            using var pad = new GamepadController();

            // GamepadFocus.PressUntil re-focuses and re-presses if the expected effect hasn't
            // landed yet — without it, the gamepad A press can land on whatever the window
            // defaulted to (ReShade, confirmed live 2026-08-10 when this same test installed
            // ReShade on Castle Crashers instead of DXVK). See its own doc comment for why.
            DxvkRecordDto record = null;
            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "DxvkToggle",
                    () => (record = FindRecordByGameName(GameName)) != null,
                    perAttemptTimeout: TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnDxvkToggle_InstallsThenUninstalls_ARealDxvkDrop), output);
                throw;
            }

            Assert.Equal(ToggleState.On, togglePattern.ToggleState);
            Assert.NotEmpty(record.InstalledDlls);
            foreach (var dll in record.InstalledDlls)
            {
                var fullPath = Path.Combine(record.InstallPath, dll);
                Assert.True(File.Exists(fullPath),
                    $"DXVK+Lilium install record lists '{dll}' but it isn't on disk at '{fullPath}'.");
            }
            output.WriteLine($"DXVK+Lilium installed via gamepad at '{record.InstallPath}' ({string.Join("+", record.InstalledDlls)}).");

            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "DxvkToggle",
                    () => FindRecordByGameName(GameName) == null,
                    perAttemptTimeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnDxvkToggle_InstallsThenUninstalls_ARealDxvkDrop), output);
                throw;
            }

            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);

            foreach (var dll in record.InstalledDlls)
            {
                var fullPath = Path.Combine(record.InstallPath, dll);
                var wasBackedUp = record.BackedUpFiles != null && record.BackedUpFiles.Contains(dll);

                // A backed-up file gets restored (moved back), so it should exist again after
                // uninstall; one that DXVK dropped fresh should be gone — same reasoning
                // DxvkInstallUiTests itself uses.
                var reachedExpectedState = Retry.WhileFalse(
                    () => File.Exists(fullPath) == wasBackedUp,
                    timeout: TimeSpan.FromSeconds(15),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                Assert.True(reachedExpectedState, wasBackedUp
                    ? $"Uninstall should have restored the original '{dll}' at '{fullPath}' but it's still missing."
                    : $"Uninstall removed the record but left '{dll}' behind at '{fullPath}'.");
            }
            output.WriteLine("DXVK+Lilium uninstalled via gamepad — game folder is back to its pre-test state.");
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
                // Same mid-write tolerance as FullscreenLumaToggleGamepadUiTests.
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
