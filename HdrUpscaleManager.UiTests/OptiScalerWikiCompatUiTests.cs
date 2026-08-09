using System;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Local-only smoke test for the OptiScaler wiki compat text (UI/ManagerViewModel.cs's
       OptiScalerWikiCompatText, added alongside OptiScalerWikiService) — confirms the real
       network fetch + parse + lookup actually resolves to real data for a real, owned/installed
       game, not just that the property exists.

       Game: Deep Rock Galactic — already used by OptiScalerInstallUiTests and confirmed by
       GAMES-TO-TEST.md's own research to ship "DLSS + FSR2 natively" per the OptiScaler
       compatibility wiki, so asserting the fetched text mentions DLSS is a real, meaningful
       check without hardcoding the wiki's exact upscaler list (which can grow over time, e.g. if
       an FSR4 patch lands). */
    [Collection(PlayniteUiCollection.Name)]
    public class OptiScalerWikiCompatUiTests
    {
        private const string GameName = "Deep Rock Galactic";
        private const string GameConfigKey = "DEEPROCKGALACTIC";

        private readonly ITestOutputHelper output;

        public OptiScalerWikiCompatUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void OpenManagerWindow_ForRealGame_OptiScalerWikiCompatTextResolvesRealData()
        {
            // Just confirms the env var is set up like every other real-launch test in this
            // suite — this test doesn't launch the game itself, only opens the manager window.
            LocalTestGameConfig.Resolve(GameConfigKey);

            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

            try
            {
                // The constructor kicks the wiki fetch off fire-and-forget (see
                // ManagerViewModel.RefreshOptiScalerWikiAsync) — poll rather than assume it's
                // resolved by the time the window is visible, same as every other async
                // detection lookup this window does.
                string text = null;
                var resolved = Retry.WhileFalse(
                    () =>
                    {
                        var element = managerWindow.FindFirstDescendant(cf => cf.ByAutomationId("OptiScalerWikiCompatText"));
                        text = element?.Name;
                        return text != null &&
                            !text.Contains("Checking the OptiScaler compatibility wiki") &&
                            !text.Contains("Could not check the OptiScaler compatibility wiki");
                    },
                    timeout: TimeSpan.FromSeconds(20),
                    interval: TimeSpan.FromMilliseconds(500)).Result;

                output.WriteLine($"OptiScalerWikiCompatText resolved to: \"{text}\"");

                if (!resolved)
                {
                    output.WriteLine(
                        "--- automation tree dump: manager window (OptiScaler wiki lookup never settled) ---");
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }

                Assert.True(resolved,
                    "OptiScalerWikiCompatText never resolved past its 'checking'/'could not check' " +
                    "placeholder within 20s — either the network fetch failed, or the cache/fetch " +
                    "path itself is broken. Last seen value: " + (text ?? "(null — AutomationId not found)"));

                Assert.False(string.IsNullOrWhiteSpace(text));

                // Real, meaningful assertion — not just "some text appeared". If this starts
                // failing, either GAMES-TO-TEST.md's research is stale (the wiki dropped DLSS
                // for this game, implausible) or ResolveWikiName/the parse logic broke matching
                // against this exact game name.
                Assert.True(text.IndexOf("DLSS", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"Expected OptiScalerWikiCompatText to mention DLSS for '{GameName}'. Actual: \"{text}\"");
            }
            finally
            {
                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }
    }
}
