using System;
using FlaUI.Core.AutomationElements;
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
    [Collection(PlayniteUiCollection.Name)]
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

            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, gameName, output);

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
