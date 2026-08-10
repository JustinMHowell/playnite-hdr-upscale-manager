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
    /* Gamepad counterpart to LumaInstallUiTests — see FullscreenReShadeToggleGamepadUiTests for
       why a real virtual controller is used instead of a UIA Toggle() call. Detection here goes
       through the Luma install record (luma_installs.json), not a raw candidate-DLL path guess,
       since Luma's real deploy directory is only known via LumaInstalledRecord.InstallPath — the
       same reason LumaInstallUiTests itself doesn't guess a path either.

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenLumaToggleGamepadUiTests
    {
        private const string GameName = "Vanquish";
        private const string GameConfigKey = "VANQUISH";
        private const string RecordStoreFileName = "luma_installs.json";

        private readonly ITestOutputHelper output;

        public FullscreenLumaToggleGamepadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PressA_OnLumaToggle_InstallsThenUninstalls_ARealLumaMod()
        {
            LocalTestGameConfig.Resolve(GameConfigKey);

            var preExisting = FindRecordByGameName(GameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked Luma install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs and " +
                    "uninstalls Luma itself via the toggle, so it refuses to run against a game " +
                    "with a real install it didn't create.");
            }

            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            var toggle = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("LumaToggle"));
            Assert.True(toggle != null, "LumaToggle checkbox not found in the Fullscreen manager window.");

            var togglePattern = toggle.Patterns.Toggle.Pattern;
            using var pad = new GamepadController();

            // GamepadFocus.PressUntil re-focuses and re-presses if the expected effect hasn't
            // landed yet — see its own doc comment for why a single press-and-hope isn't reliable.
            LumaRecordDto record = null;
            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "LumaToggle",
                    () => (record = FindRecordByGameName(GameName)) != null,
                    perAttemptTimeout: TimeSpan.FromSeconds(60));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnLumaToggle_InstallsThenUninstalls_ARealLumaMod), output);
                throw;
            }

            Assert.Equal(ToggleState.On, togglePattern.ToggleState);
            Assert.NotEmpty(record.InstalledFiles);
            foreach (var relativePath in record.InstalledFiles)
            {
                var fullPath = Path.Combine(record.InstallPath, relativePath);
                Assert.True(File.Exists(fullPath),
                    $"Luma install record lists '{relativePath}' but it isn't on disk at '{fullPath}'.");
            }
            output.WriteLine($"Luma installed via gamepad at '{record.InstallPath}' ({record.InstalledFiles.Count} file(s)).");

            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "LumaToggle",
                    () => FindRecordByGameName(GameName) == null,
                    perAttemptTimeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnLumaToggle_InstallsThenUninstalls_ARealLumaMod), output);
                throw;
            }

            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);

            foreach (var relativePath in record.InstalledFiles)
            {
                var fullPath = Path.Combine(record.InstallPath, relativePath);
                var fileGone = Retry.WhileFalse(
                    () => !File.Exists(fullPath),
                    timeout: TimeSpan.FromSeconds(15),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                Assert.True(fileGone, $"Uninstall removed the record but left '{relativePath}' behind at '{fullPath}'.");
            }
            output.WriteLine("Luma uninstalled via gamepad — game folder is clean.");
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
