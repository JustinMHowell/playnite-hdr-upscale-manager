using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Local-only smoke test: drives a real, already-running Playnite to confirm the addon's
       per-game manager window actually renders every component section. Not run in CI, not
       referenced by the addon — see README.md in this folder for setup and required env vars.

       Playnite's own automation tree isn't something this repo can pin against (no source
       here, and it can change between Playnite versions), so the game-list and context-menu
       lookups below are best-effort name matches. On failure they dump the live tree via
       AutomationTreeDumper so you can fix the one selector that broke instead of guessing. */
    public class ManagerWindowUiTests : IClassFixture<PlayniteFixture>
    {
        private static readonly string[] ExpectedSectionTitles =
        {
            "ReShade",
            "RenoDX",
            "Luma",
            "DXVK+Lilium HDR",
            "OptiScaler (AMD)",
            "OS HDR toggle on launch/exit",
        };

        private readonly PlayniteFixture fixture;
        private readonly ITestOutputHelper output;

        public ManagerWindowUiTests(PlayniteFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        [Fact]
        public void OpenManagerWindow_ForConfiguredGame_ShowsAllComponentSections()
        {
            var gameName = Environment.GetEnvironmentVariable("HUM_UITEST_GAME");
            if (string.IsNullOrWhiteSpace(gameName))
            {
                throw new InvalidOperationException(
                    "Set HUM_UITEST_GAME to the exact title of a game in your Playnite library " +
                    "before running this test, e.g.:\n" +
                    "  $env:HUM_UITEST_GAME = 'Cyberpunk 2077'\n" +
                    "See README.md in HdrUpscaleManager.UiTests/.");
            }

            var mainWindow = fixture.GetMainWindow();
            var managerWindow = OpenManagerWindowFor(mainWindow, gameName);

            try
            {
                foreach (var title in ExpectedSectionTitles)
                {
                    AssertSectionPresent(managerWindow, title);
                }
            }
            finally
            {
                managerWindow.Close();
            }
        }

        private Window OpenManagerWindowFor(Window mainWindow, string gameName)
        {
            var gameElement = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName(gameName)),
                timeout: TimeSpan.FromSeconds(15)).Result;

            if (gameElement == null)
            {
                Dump("main window", mainWindow);
                throw new InvalidOperationException(
                    $"Couldn't find '{gameName}' in the Playnite library view. Make sure it's " +
                    "visible (not filtered/scrolled out) in the currently active view.");
            }

            Mouse.Click(gameElement.GetClickablePoint(), MouseButton.Right);

            var desktop = fixture.Automation.GetDesktop();
            var submenuHeader = Retry.WhileNull(
                () => desktop.FindFirstDescendant(cf => cf.ByName("HDR & Upscale Manager")),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (submenuHeader == null)
            {
                Dump("desktop (after right-click)", desktop);
                throw new InvalidOperationException(
                    "Couldn't find the 'HDR & Upscale Manager' context-menu submenu after " +
                    "right-clicking the game. See the dumped tree above to fix the selector.");
            }

            Mouse.Click(submenuHeader.GetClickablePoint(), MouseButton.Left);

            var openItem = Retry.WhileNull(
                () => desktop.FindFirstDescendant(cf => cf.ByName("Open in HDR & Upscale Manager…")),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (openItem == null)
            {
                Dump("desktop (after opening submenu)", desktop);
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
                Dump("desktop (waiting for manager window)", desktop);
                throw new InvalidOperationException(
                    $"The manager window ('{titlePrefix}...') never appeared after clicking the menu item.");
            }

            return managerWindowElement.AsWindow();
        }

        private void AssertSectionPresent(Window managerWindow, string sectionTitle)
        {
            var found = Retry.WhileNull(
                () => managerWindow.FindFirstDescendant(cf => cf.ByName(sectionTitle)),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (found == null)
            {
                Dump("manager window", managerWindow);
            }

            Assert.NotNull(found);
        }

        private void Dump(string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }
    }
}
