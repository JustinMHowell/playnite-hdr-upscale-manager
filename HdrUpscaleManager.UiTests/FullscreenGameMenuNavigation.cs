using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Fullscreen counterpart to ManagerWindowNavigation — reaches the addon's per-game manager
       window in Fullscreen mode. What matters for these tests is whether a real gamepad drives
       the addon's own UI correctly (see UI/FocusActivation.cs, HdrUpscaleManagerPlugin.Window.cs's
       OnControllerButtonStateChanged) — getting there through Playnite's own menus is plumbing,
       not the thing under test, so navigation up to the addon's window uses whatever's reliable.

       This machine's Fullscreen theme is a heavily customized one (Aniki-ReMake,
       github.com/Mike-Aniki/Aniki-ReMake — cloned into reference/Aniki-ReMake for reference)
       rather than Playnite's stock Fullscreen layout. Every gamepad button tried for opening the
       per-game menu on the main window (Y, Start, Back), and a real mouse right-click on the game
       item, all left the app on just its main window, reproducibly. An earlier version of this
       method worked around that by invoking the always-visible bottom-bar "Game Options" button
       instead — but that button (ButtonBottomMenuDual in Aniki's Main.xaml) turned out to be a
       pure visual legend with no Command of its own; invoking it does nothing, confirmed live
       2026-08-10 when it left the Game Options menu never appearing. The real trigger is
       OpenGameMenuCommand's own KeyBinding/GameControllerInputBinding on the game grid item
       itself (gamepad Y, keyboard "Apps" per Main.xaml — this theme also answers to a plain "X"
       keypress, confirmed live against a real controller), which does need real routed-input
       focus. Sending that keypress right after search+select works because the fixture launches
       a fresh Playnite process the test itself owns, so the window still holds real focus at
       that point — unlike the already-running-app case the SetForegroundWindow/ActiveSource
       problem above describes.

       Locating the target game goes through Fullscreen's own on-screen search rather than D-pad
       grid traversal — the library is large enough that a plain D-pad scroll or the grid's
       "Load more" button (which does nothing observable via automation, confirmed live) would
       need hundreds of presses to reach an arbitrary game. Search narrows the grid directly, but
       only works with FilterSettings reset first (see PlayniteFullscreenFilter) — a leftover
       Feature-tag filter combines with the search text to produce zero matches even for a query
       that should obviously match. */
    internal static class FullscreenGameMenuNavigation
    {
        public static Window Open(
            PlayniteFullscreenFixture fixture,
            string gameName,
            ITestOutputHelper output)
        {
            var mainWindow = fixture.GetMainWindow();
            mainWindow.SetForeground();
            Thread.Sleep(300);

            var gameItem = SearchForGame(mainWindow, gameName, output);
            gameItem.Patterns.SelectionItem.PatternOrDefault?.Select();
            Thread.Sleep(300);
            gameItem.Focus();
            Thread.Sleep(200);

            // Confirmed live 2026-08-10 against a real controller: the always-visible bottom-bar
            // "Game Options" hint (ButtonBottomMenuDual in Aniki's Main.xaml) is a pure visual
            // legend with no Command of its own — invoking it does nothing, which is what an
            // earlier version of this method actually did. The real trigger is
            // OpenGameMenuCommand's own KeyBinding/GameControllerInputBinding on the game grid
            // item (gamepad Y or keyboard "Apps"); this theme also answers to a plain "X"
            // keypress. From the Options popup it's a plain Down/Enter menu: Extensions → this
            // addon's name → "Open in HDR & Upscale Manager…".
            PressKey(VirtualKeyShort.KEY_X);
            Thread.Sleep(500);

            SelectMenuItemAndConfirm(fixture, "Extensions", output);
            Thread.Sleep(500);

            SelectMenuItemAndConfirm(fixture, "HDR & Upscale Manager", output);
            Thread.Sleep(500);

            SelectMenuItemAndConfirm(fixture, "Open in HDR & Upscale Manager", output, exactMatch: false);

            var managerWindow = FindOwnedWindow(
                fixture,
                w => (w.Name ?? "").StartsWith($"HDR & Upscale Manager — {gameName}", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(15));

            if (managerWindow == null)
            {
                output.WriteLine("--- automation tree dump: main window (waiting for manager window) ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException("The addon's Manager window never opened.");
            }

            return managerWindow;
        }

        // Repeatedly presses Down until the named item is the one carrying real keyboard focus
        // or UIA selection (rather than a fixed press count), then confirms it with Enter — the
        // same two signals a real controller's D-pad/A would drive, and robust to a menu whose
        // item count/order isn't fixed (e.g. the Extensions list depends on which plugins are
        // installed). Searches every window owned by the Playnite process, not just the main
        // window, since these popups may or may not be separate owned windows.
        private static void SelectMenuItemAndConfirm(
            PlayniteFullscreenFixture fixture, string itemName, ITestOutputHelper output, bool exactMatch = true)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidate = FindElementAcrossWindows(
                    fixture,
                    e => exactMatch ? e.Name == itemName : (e.Name ?? "").Contains(itemName));

                if (candidate != null && IsCurrentlyHighlighted(candidate))
                {
                    PressKey(VirtualKeyShort.RETURN);
                    return;
                }

                PressKey(VirtualKeyShort.DOWN);
                Thread.Sleep(200);
            }

            output.WriteLine($"--- automation tree dump: main window (couldn't highlight \"{itemName}\" via Down) ---");
            output.WriteLine(AutomationTreeDumper.Dump(fixture.GetMainWindow()));
            throw new InvalidOperationException($"Never got \"{itemName}\" highlighted after 20 Down presses.");
        }

        private static bool IsCurrentlyHighlighted(AutomationElement element)
        {
            for (var current = element; current != null; current = current.Parent)
            {
                if (current.Properties.HasKeyboardFocus.ValueOrDefault) return true;

                var selectionItem = current.Patterns.SelectionItem.PatternOrDefault;
                if (selectionItem != null && selectionItem.IsSelected.ValueOrDefault) return true;
            }

            return false;
        }

        private static AutomationElement FindElementAcrossWindows(PlayniteFullscreenFixture fixture, Func<AutomationElement, bool> match)
        {
            var desktop = fixture.Automation.GetDesktop();
            var playniteProcessId = fixture.App.ProcessId;

            return desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Where(w => w.Properties.ProcessId.ValueOrDefault == playniteProcessId)
                .SelectMany(w => w.FindAllDescendants())
                .FirstOrDefault(match);
        }

        private static void PressKey(VirtualKeyShort key)
        {
            FlaUI.Core.Input.Keyboard.Press(key);
            Thread.Sleep(80);
            FlaUI.Core.Input.Keyboard.Release(key);
        }

        private static AutomationElement SearchForGame(Window mainWindow, string gameName, ITestOutputHelper output)
        {
            var searchButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PART_ButtonSearch"));
            if (searchButton == null)
            {
                output.WriteLine("--- automation tree dump: main window (looking for PART_ButtonSearch) ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException("Couldn't find the Search button (PART_ButtonSearch).");
            }

            // Coordinate-based clicks are unreliable in this theme (Viewbox scaling puts UIA's
            // reported screen coordinates out of step with the actually-rendered pixels) — the
            // Invoke pattern activates the control logically instead.
            searchButton.Patterns.Invoke.Pattern.Invoke();
            Thread.Sleep(800);

            FullscreenOnScreenKeyboard.Type(mainWindow, gameName, output);
            FullscreenOnScreenKeyboard.Submit(mainWindow, output);

            var list = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PART_ListGameItems"));
            var match = Retry.WhileNull(
                () => list?.FindAllChildren().FirstOrDefault(i => i.Name == gameName),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(300)).Result;

            if (match == null)
            {
                output.WriteLine("--- automation tree dump: main window (after search) ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException($"'{gameName}' wasn't found in the game list after searching for it.");
            }

            return match;
        }

        private static Window FindOwnedWindow(PlayniteFullscreenFixture fixture, Func<Window, bool> match, TimeSpan timeout)
        {
            var desktop = fixture.Automation.GetDesktop();
            var playniteProcessId = fixture.App.ProcessId;

            return Retry.WhileNull(
                () => desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                    .Where(w => w.Properties.ProcessId.ValueOrDefault == playniteProcessId)
                    .Select(w => w.AsWindow())
                    .FirstOrDefault(match),
                timeout: timeout,
                interval: TimeSpan.FromMilliseconds(300)).Result;
        }
    }
}
