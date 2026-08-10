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
    /* Gamepad counterpart to RenoDxInstallUiTests — see FullscreenReShadeToggleGamepadUiTests for
       why a real virtual controller is used instead of a UIA Toggle() call. RenoDX has no install
       variant choice for a wiki-matched game like Hollow Knight (see RenoDxInstallUiTests), so
       unlike FullscreenDxvkToggleGamepadUiTests/FullscreenLumaToggleGamepadUiTests this toggle
       drives RenoDxInstallCommand directly on Checked, same shape as ReShade/Luma/DXVK — the
       engine-detect fallback buttons (Unreal/Unity) stay separate buttons in the XAML because
       THEY are a real variant choice, but the plain wiki-matched path isn't.

       Also confirms RenoDX's ReShade dependency the same way RenoDxInstallUiTests does: RenoDX
       ships as a ReShade addon and does nothing without ReShade already hosting it.

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenRenoDxToggleGamepadUiTests
    {
        private const string GameName = "Hollow Knight";
        private const string GameConfigKey = "HOLLOWKNIGHT";
        private const string RecordStoreFileName = "renodx_installs.json";

        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };

        private readonly ITestOutputHelper output;

        public FullscreenRenoDxToggleGamepadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PressA_OnRenoDxToggle_InstallsThenUninstalls_ARealRenoDxAddon()
        {
            LocalTestGameConfig.Resolve(GameConfigKey);

            var preExisting = FindRecordByGameName(GameName);
            if (preExisting != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked RenoDX install (InstallPath: " +
                    $"{preExisting.InstallPath}) before this test started. This test installs and " +
                    "uninstalls RenoDX itself via the toggle, so it refuses to run against a game " +
                    "with a real install it didn't create.");
            }

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            var toggle = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("RenoDxToggle"));
            Assert.True(toggle != null, "RenoDxToggle checkbox not found in the Fullscreen manager window.");

            var togglePattern = toggle.Patterns.Toggle.Pattern;
            using var pad = new GamepadController();

            // GamepadFocus.PressUntil re-focuses and re-presses if the expected effect hasn't
            // landed yet — RenoDX's install runs two sequential async steps (auto-ReShade, then
            // the addon), which roughly doubles the odds of a single press missing; see
            // GamepadFocus's own doc comment for the live failure this fixes.
            RenoDxRecordDto record = null;
            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "RenoDxToggle",
                    () => (record = FindRecordByGameName(GameName)) != null,
                    perAttemptTimeout: TimeSpan.FromSeconds(60));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnRenoDxToggle_InstallsThenUninstalls_ARealRenoDxAddon), output);
                throw;
            }

            Assert.Equal(ToggleState.On, togglePattern.ToggleState);
            Assert.True(File.Exists(record.InstallPath), $"RenoDX install wrote a record but the addon file isn't on disk at '{record.InstallPath}'.");

            var gameInstallDirectory = Path.GetDirectoryName(record.InstallPath);
            var reShadeProxyDllPath = ReShadeProxyDllCandidates
                .Select(name => Path.Combine(gameInstallDirectory, name))
                .FirstOrDefault(File.Exists);
            Assert.True(reShadeProxyDllPath != null,
                $"RenoDX needs ReShade installed as a host, but none of {string.Join(", ", ReShadeProxyDllCandidates)} " +
                $"exist in '{gameInstallDirectory}' after installing RenoDX via the gamepad toggle.");

            output.WriteLine($"RenoDX installed via gamepad at '{record.InstallPath}' (ReShade host: '{reShadeProxyDllPath}').");

            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "RenoDxToggle",
                    () => FindRecordByGameName(GameName) == null,
                    perAttemptTimeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnRenoDxToggle_InstallsThenUninstalls_ARealRenoDxAddon), output);
                throw;
            }

            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);

            var addonFileGone = Retry.WhileFalse(
                () => !File.Exists(record.InstallPath),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(250)).Result;
            Assert.True(addonFileGone, $"Uninstall removed the tracked record but left the addon file behind at '{record.InstallPath}'.");

            // Same CreationTimeUtc reasoning as RenoDxInstallUiTests — only remove a ReShade
            // install THIS run actually triggered, never one that pre-existed for other reasons.
            if (File.GetCreationTimeUtc(reShadeProxyDllPath) >= testStartedUtc.AddSeconds(-2))
            {
                File.Delete(reShadeProxyDllPath);
            }

            output.WriteLine("RenoDX uninstalled via gamepad — game folder is clean.");
        }

        private static RenoDxRecordDto FindRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(RecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, RenoDxRecordDto>>(File.ReadAllText(path));
                return all?.Values.FirstOrDefault(r =>
                    string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Same mid-write tolerance as FullscreenLumaToggleGamepadUiTests.
                return null;
            }
        }

        private class RenoDxRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
        }
    }
}
