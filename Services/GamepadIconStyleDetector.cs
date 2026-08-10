using System;

namespace HdrUpscaleManager.Services
{
    public enum GamepadIconStyle
    {
        Xbox,
        PlayStation,
    }

    /* Playnite's SDK exposes no controller-brand API — GamepadController only carries a raw
       device Name string sourced from its own controller layer (SDL2's game-controller DB), see
       IPlayniteAPI.GetConnectedControllers(). This is a best-effort guess from that string, not a
       guarantee. Confirmed live 2026-08-10 against a virtual DualShock4 (Nefarius.ViGEm.Client,
       Sony's real VID_054C/PID_05C4) — Windows enumerates it as a plain "HID-compliant game
       controller" with no brand in the PNP friendly name, so this only works at all because
       Playnite's own controller layer resolves a proper name from the device itself, not from
       Windows' generic PNP description. Defaults to Xbox, matching XInput's own A/B/X/Y naming
       that every generic/unbranded pad reports as. */
    public static class GamepadIconStyleDetector
    {
        private static readonly string[] PlayStationNameFragments =
        {
            "playstation", "dualshock", "dualsense", "wireless controller", "ps4", "ps5",
        };

        public static GamepadIconStyle Detect(string controllerName)
        {
            if (string.IsNullOrEmpty(controllerName)) return GamepadIconStyle.Xbox;

            var nameLower = controllerName.ToLowerInvariant();
            return Array.Exists(PlayStationNameFragments, fragment => nameLower.Contains(fragment))
                ? GamepadIconStyle.PlayStation
                : GamepadIconStyle.Xbox;
        }
    }
}
