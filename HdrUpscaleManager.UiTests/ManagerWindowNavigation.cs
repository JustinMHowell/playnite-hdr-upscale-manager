using System;
using System.Diagnostics;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Reaches the addon's per-game manager window via Playnite's own global search
       (playnite://playnite/search/<term>, a registered URI forwarded over Playnite's existing
       single-instance named pipe straight into MainView.OpenSearch) rather than right-clicking
       the game directly in the library grid, which can be UI-virtualized on a library with
       hundreds/thousands of games (see GAMES-TO-TEST.md) — a game that isn't currently
       scrolled into view may not even have an automation element to find at all.

       The actual interaction sequence here (search URI -> Enter -> a per-game sidebar opens
       with a "More" button -> that opens the same context menu right-clicking would) was
       confirmed live 2026-08-08 by directly observing the search URI's behavior — NOT derived
       from source, since nothing in Playnite's own source pins down what the search overlay's
       default Enter action or its result sidebar actually look like. An earlier attempt guessed
       at a "Game menu" toggle button from SearchWindow.xaml's bindings without ever seeing it
       run; that guess didn't hold up live (the button either never opened a stable menu or it
       closed again before the next lookup) and was abandoned in favor of this confirmed path. */
    internal static class ManagerWindowNavigation
    {
        public static Window Open(PlayniteFixture fixture, string gameName, ITestOutputHelper output)
        {
            var desktop = fixture.Automation.GetDesktop();

            // The URI is delivered by launching a brand-new throwaway Playnite.DesktopApp.exe,
            // which detects the already-running instance's mutex and forwards the command over
            // its named pipe (see PlayniteApplication.cs) — confirmed live 2026-08-08 that this
            // can race Playnite's own pipe-server setup right after a fresh launch: the search
            // window opens but stays on its empty placeholder text, with no query ever
            // delivered, regardless of what name was searched for. Re-firing the URI a few
            // times (each with its own short wait) resolves that race instead of assuming the
            // first shot landed.
            //
            // Deliberately checking for ANY result row (ControlType.ListItem) rather than an
            // exact name match against gameName: a live search result's own accessible Name
            // can carry extra decoration beyond the plain title (confirmed live 2026-08-08 —
            // one result's Name included digits that don't appear in the game's actual Name,
            // which is what the addon's own window title/install records use for verification
            // further down). The search itself does fuzzy matching against the real Game.Name,
            // so an exact string here would just be redundant with — and less reliable than —
            // that fuzzy ranking.
            AutomationElement searchResult = null;
            for (var attempt = 1; attempt <= 3 && searchResult == null; attempt++)
            {
                Process.Start(new ProcessStartInfo(
                    $"playnite://playnite/search/{Uri.EscapeDataString(gameName)}") { UseShellExecute = true });

                searchResult = Retry.WhileNull(
                    () =>
                    {
                        // Scoped to the search window specifically, not the whole desktop —
                        // ControlType.ListItem alone is far too broad (taskbar, Start menu,
                        // any other open app's list views all have them too).
                        var searchWindow = desktop.FindFirstDescendant(cf => cf.ByName("Playnite search"));
                        return searchWindow?.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem));
                    },
                    timeout: TimeSpan.FromSeconds(8)).Result;
            }

            if (searchResult == null)
            {
                Dump(output, "desktop (after search URI)", desktop);
                throw new InvalidOperationException(
                    $"No search result appeared for '{gameName}' after firing " +
                    $"playnite://playnite/search/... 3 times — either the URI never reached " +
                    "the running instance, or no game matched. See the dumped tree above.");
            }

            // Runs the top result's default (Enter) action, which opens the library's per-game
            // sidebar for that result — confirmed live, not guessed.
            Keyboard.Type(VirtualKeyShort.ENTER);

            var moreButton = Retry.WhileNull(
                () => desktop.FindFirstDescendant(cf => cf.ByName("More")),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (moreButton == null)
            {
                Dump(output, "desktop (after Enter, waiting for sidebar's More button)", desktop);
                throw new InvalidOperationException(
                    $"The sidebar's 'More' button never appeared after pressing Enter on the " +
                    $"'{gameName}' search result. See the dumped tree above to fix the selector.");
            }

            Mouse.Click(moreButton.GetClickablePoint(), MouseButton.Left);

            var submenuHeader = Retry.WhileNull(
                () => desktop.FindFirstDescendant(cf => cf.ByName("HDR & Upscale Manager")),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (submenuHeader == null)
            {
                Dump(output, "desktop (after clicking More)", desktop);
                throw new InvalidOperationException(
                    "Couldn't find the 'HDR & Upscale Manager' context-menu submenu after " +
                    "clicking the sidebar's 'More' button. See the dumped tree above to fix the selector.");
            }

            Mouse.Click(submenuHeader.GetClickablePoint(), MouseButton.Left);

            var openItem = Retry.WhileNull(
                () => desktop.FindFirstDescendant(cf => cf.ByName("Open in HDR & Upscale Manager…")),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (openItem == null)
            {
                Dump(output, "desktop (after opening submenu)", desktop);
                throw new InvalidOperationException(
                    "Couldn't find the 'Open in HDR & Upscale Manager…' menu item. See the " +
                    "dumped tree above to fix the selector.");
            }

            Mouse.Click(openItem.GetClickablePoint(), MouseButton.Left);

            var titlePrefix = $"HDR & Upscale Manager — {gameName}";
            var managerWindowElement = Retry.WhileNull(
                () => desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                    .FirstOrDefault(w => (w.Name ?? "").StartsWith(titlePrefix, StringComparison.Ordinal)),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (managerWindowElement == null)
            {
                Dump(output, "desktop (waiting for manager window)", desktop);
                throw new InvalidOperationException(
                    $"The manager window ('{titlePrefix}...') never appeared after clicking the menu item.");
            }

            return managerWindowElement.AsWindow();
        }

        private static void Dump(ITestOutputHelper output, string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }
    }
}
