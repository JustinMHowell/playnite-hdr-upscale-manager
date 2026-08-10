using System;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* End-to-end proof that ManagerViewModel.RefreshGamepadIconStyle (see
       Services/GamepadIconStyleDetector.cs) actually reacts to a real connected controller, not
       just to a hand-fed name string in a unit test — both at window-open time (an Xbox 360
       controller already connected before the window opens) and live, while the window stays
       open (swapping to a DualShock4 fires HdrUpscaleManagerPlugin.OnControllerConnected, which
       forwards to the already-open ViewModel — see Plugin/HdrUpscaleManagerPlugin.Window.cs).

       Requires the ViGEmBus driver (https://vigembus.com/) installed — see GamepadController /
       VirtualDualShock4. */
    [Collection(PlayniteUiCollection.Name)]
    public class FullscreenGamepadIconDetectionUiTests
    {
        private const string GameName = "Vampire Survivors";
        private const string GameConfigKey = "VAMPIRESURVIVORS";

        private readonly ITestOutputHelper output;

        public FullscreenGamepadIconDetectionUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void GamepadHint_SwitchesBetweenXboxAndPlayStationIcons_AsControllersConnect()
        {
            LocalTestGameConfig.Resolve(GameConfigKey);

            var xboxPad = new GamepadController();
            using var fixture = new PlayniteFullscreenFixture();

            var managerWindow = FullscreenGameMenuNavigation.Open(fixture, GameName, output);
            managerWindow.SetForeground();

            // Visibility="Collapsed" elements still show up in FlaUI's default (raw) tree walk —
            // they're just not actually on screen — so "found" isn't the right signal here;
            // IsOffscreen is what actually reflects the WPF Visibility binding.
            bool IsHintOnScreen(string automationId)
            {
                var element = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                return element != null && !element.Properties.IsOffscreen.ValueOrDefault;
            }

            var xboxVisibleAtOpen = Retry.WhileFalse(
                () => IsHintOnScreen("GamepadHintXbox"),
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(250)).Result;

            if (!xboxVisibleAtOpen)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(GamepadHint_SwitchesBetweenXboxAndPlayStationIcons_AsControllersConnect), output);
            }
            Assert.True(xboxVisibleAtOpen, "GamepadHintXbox wasn't visible at window-open with only a virtual Xbox 360 controller connected.");
            Assert.False(IsHintOnScreen("GamepadHintPlayStation"));
            output.WriteLine("Xbox hint confirmed at window-open with only an Xbox 360 controller connected.");

            xboxPad.Dispose();
            using var ds4Pad = new VirtualDualShock4();

            var playStationVisibleLive = Retry.WhileFalse(
                () => IsHintOnScreen("GamepadHintPlayStation"),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500)).Result;

            if (!playStationVisibleLive)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(GamepadHint_SwitchesBetweenXboxAndPlayStationIcons_AsControllersConnect), output);
            }
            Assert.True(playStationVisibleLive,
                "GamepadHintPlayStation never appeared after swapping to a virtual DualShock4 while the window stayed open — " +
                "OnControllerConnected may not be reaching the open ViewModel.");
            Assert.False(IsHintOnScreen("GamepadHintXbox"));
            output.WriteLine("PlayStation hint confirmed live after swapping to a DualShock4 with the window already open.");
        }
    }
}
