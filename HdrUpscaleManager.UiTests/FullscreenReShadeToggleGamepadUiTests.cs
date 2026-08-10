using System;
using System.IO;
using System.Linq;
using FlaUI.Core.Definitions;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* End-to-end proof that a real gamepad (a virtual Xbox 360 controller via ViGEmBus, not a
       UIA-programmatic Invoke/Toggle call) can drive the addon's Fullscreen manager window:
       reaching it (see FullscreenGameMenuNavigation) uses whatever's reliable — Playnite's own
       menus aren't the thing under test — but flipping the "Installed" toggle uses a real
       gamepad A press specifically. That exercises UI/FocusActivation.cs and
       HdrUpscaleManagerPlugin.Window.cs's OnControllerButtonStateChanged, which Playnite delivers
       to every loaded plugin directly rather than through WPF's own input system, so a UIA
       Invoke()/Toggle() call on the checkbox would prove nothing about whether a real controller
       button actually reaches it.

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenReShadeToggleGamepadUiTests
    {
        private const string GameName = "Vampire Survivors";
        private const string GameConfigKey = "VAMPIRESURVIVORS";

        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };

        private readonly ITestOutputHelper output;

        public FullscreenReShadeToggleGamepadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PressA_OnReShadeToggle_InstallsThenUninstalls_ARealReShadeProxy()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var gameInstallDirectory = game.InstallDirectory;

            var preExistingProxyPath = FindExistingReShadeProxy(gameInstallDirectory);
            if (preExistingProxyPath != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a ReShade proxy DLL at '{preExistingProxyPath}' before this " +
                    "test started. This test installs and uninstalls ReShade itself via the toggle, so it " +
                    "refuses to run against a game with a real install it didn't create.");
            }

            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            var toggle = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("ReShadeToggle"));
            Assert.True(toggle != null, "ReShadeToggle checkbox not found in the Fullscreen manager window.");

            var togglePattern = toggle.Patterns.Toggle.Pattern;
            using var pad = new GamepadController();

            // GamepadFocus.PressUntil re-focuses and re-presses if the expected effect hasn't
            // landed yet — see its own doc comment for why a single press-and-hope isn't reliable.
            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "ReShadeToggle",
                    () => FindExistingReShadeProxy(gameInstallDirectory) != null,
                    perAttemptTimeout: TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnReShadeToggle_InstallsThenUninstalls_ARealReShadeProxy), output);
                throw;
            }

            Assert.Equal(ToggleState.On, togglePattern.ToggleState);
            output.WriteLine($"ReShade installed via gamepad: '{FindExistingReShadeProxy(gameInstallDirectory)}'.");

            try
            {
                GamepadFocus.PressUntil(toggle, pad, output, "ReShadeToggle",
                    () => FindExistingReShadeProxy(gameInstallDirectory) == null,
                    perAttemptTimeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(PressA_OnReShadeToggle_InstallsThenUninstalls_ARealReShadeProxy), output);
                throw;
            }

            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);
            output.WriteLine("ReShade uninstalled via gamepad — game folder is clean.");
        }

        private static string FindExistingReShadeProxy(string gameInstallDirectory) =>
            ReShadeProxyDllCandidates
                .Select(name => Path.Combine(gameInstallDirectory, name))
                .FirstOrDefault(File.Exists);
    }
}
