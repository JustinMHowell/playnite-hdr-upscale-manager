using System;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* A single upfront AutomationElement.Focus() call isn't reliable for a whole test that fires
       two gamepad presses in sequence — confirmed live 2026-08-10 on
       FullscreenLumaToggleGamepadUiTests: the first press correctly hit LumaToggle (Luma
       installed), but by the second press WPF's logical focus (what FocusActivation.InvokeFocused
       reads via FocusManager.GetFocusedElement, not FlaUI's own idea of "focused") had drifted
       back to ReShadeToggle — the window's Loaded-time default-focused control — so the second
       press installed ReShade on the test's game instead of uninstalling Luma.

       Re-focusing immediately before every press (FocusAndConfirm) closed that for
       ReShade/Luma/DXVK/OptiScaler, but RenoDX still misfired the same way even with a fresh
       Focus() immediately beforehand — confirmed live 2026-08-10 on Hollow Knight, where the
       second press re-ran ReShadeInstallCommand instead of RenoDxUninstallCommand. RenoDX's
       install runs two sequential async steps internally (auto-ReShade, then the addon itself),
       roughly doubling the odds of the same focus drift landing inside the retry window. PressUntil
       treats a gamepad press the same way ClickUninstallUntilRecordGone treats a mouse click
       elsewhere in this suite: re-establish focus and press again if the expected effect hasn't
       landed yet, rather than trusting one press to always reach its target. */
    internal static class GamepadFocus
    {
        public static void FocusAndConfirm(AutomationElement element, ITestOutputHelper output, string label)
        {
            const int maxAttempts = 5;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                element.Focus();
                Thread.Sleep(200);

                if (element.Properties.HasKeyboardFocus.ValueOrDefault) return;

                output.WriteLine($"'{label}' didn't report real WPF keyboard focus after Focus() (attempt {attempt}/{maxAttempts}) — retrying.");
            }

            throw new InvalidOperationException(
                $"Couldn't get real WPF keyboard focus onto '{label}' after {maxAttempts} attempts — " +
                "a gamepad A press right now would hit whatever control actually holds focus instead.");
        }

        public static void PressUntil(
            AutomationElement element,
            GamepadController pad,
            ITestOutputHelper output,
            string label,
            Func<bool> succeeded,
            TimeSpan perAttemptTimeout,
            int maxAttempts = 3)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                FocusAndConfirm(element, output, label);
                pad.PressAndRelease(Xbox360Button.A, holdMs: 300, settleMs: 500);

                var reached = Retry.WhileFalse(succeeded, timeout: perAttemptTimeout, interval: TimeSpan.FromMilliseconds(500)).Result;
                if (reached) return;

                output.WriteLine($"Gamepad A press on '{label}' (attempt {attempt}/{maxAttempts}) didn't reach the expected state within {perAttemptTimeout.TotalSeconds:0}s — WPF's logical focus may have drifted; retrying.");
            }

            throw new InvalidOperationException(
                $"Gamepad A press on '{label}' never reached the expected state after {maxAttempts} attempts.");
        }
    }
}
