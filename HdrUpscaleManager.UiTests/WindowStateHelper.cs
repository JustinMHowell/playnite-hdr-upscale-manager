using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Maximizes a window right after it's found — every Playnite window this suite drives
       (main window, the per-game manager window, the Add-ons window) gets this so position-based
       lookups (MainMenuNavigation's percentage-of-window-size trigger search, DxvkSectionVisibilityUiTests'
       own scrolling, etc.) always work against the same full-screen layout instead of whatever
       size/position the window happened to restore to last time, and so a human watching a test
       run can actually see what's on screen. */
    internal static class WindowStateHelper
    {
        public static void Maximize(Window window, ITestOutputHelper output = null)
        {
            try
            {
                window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            }
            catch (Exception ex)
            {
                output?.WriteLine($"Failed to maximize window '{window.Name}': {ex.Message}");
            }
        }
    }
}
