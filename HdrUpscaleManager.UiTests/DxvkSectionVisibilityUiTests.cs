using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Regression test for ShowDxvkSection (UI/ManagerViewModel.cs) — confirmed live 2026-08-08
       that Control (Ultimate Edition) sat there showing a disabled "Install (DX8/DX9/DX10)"
       button forever: its own exe delegates rendering to an internal Remedy engine DLL, so local
       PE inspection can't tell the graphics API at all (stays Unknown), and PCGamingWiki lists
       both "11" and "12" for it — ambiguous about which, but not about being above DXVK's
       D3D8/9/10 ceiling either way. PcGamingWikiService.InferConfidentlyAboveDxvkRange now tells
       that apart from "can't tell", so the whole section hides instead of sitting there disabled.
       Needs the real PCGamingWiki round trip to complete (local detection never resolves Control
       at all), so this waits rather than asserting immediately. */
    [Collection(PlayniteUiCollection.Name)]
    public class DxvkSectionVisibilityUiTests
    {
        private const string GameName = "Control Ultimate Edition";

        private readonly ITestOutputHelper output;

        public DxvkSectionVisibilityUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void ManagerWindow_ForControl_HidesDxvkSectionOnceWikiConfirmsItsAboveRange()
        {
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

            try
            {
                // The real PCGamingWiki round trip (opensearch + two Cargo queries) can take a
                // while longer than the manager window itself takes to open — generous timeout
                // rather than a tight one tuned to a single observed run.
                var hidden = Retry.WhileFalse(
                    () => !IsDxvkHeadingVisible(managerWindow),
                    timeout: TimeSpan.FromSeconds(60),
                    interval: TimeSpan.FromMilliseconds(500)).Result;

                if (!hidden)
                {
                    output.WriteLine("--- automation tree dump: manager window (DXVK section still present) ---");
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }

                Assert.True(hidden,
                    "Expected the DXVK+Lilium HDR section to hide for Control once the PCGamingWiki " +
                    "lookup confirms its Direct3D versions (11, 12) are both above DXVK's range — " +
                    "see PcGamingWikiService.InferConfidentlyAboveDxvkRange.");
            }
            finally
            {
                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }

        /* A Collapsed WPF element can still show up in FlaUI's default (raw) tree walk —
           confirmed elsewhere in this codebase (see ManifestSyncUiTests.IsActuallyVisible) for
           Playnite's own menu system, and the same applies here to our own Border's
           Visibility binding: FindFirstDescendant "finding" the heading doesn't mean it's
           actually on screen. A zero-size bounding rect or IsOffscreen is what actually says so. */
        private static bool IsDxvkHeadingVisible(Window managerWindow)
        {
            var heading = managerWindow.FindFirstDescendant(cf => cf.ByName("DXVK+Lilium HDR"));
            if (heading == null) return false;

            try
            {
                var rect = heading.BoundingRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) return false;
                if (heading.Properties.IsOffscreen.ValueOrDefault) return false;
            }
            catch
            {
                // Property not supported / element torn down mid-check — treat as not
                // reliably visible rather than throwing out of a Retry poll callback.
                return false;
            }

            return true;
        }
    }
}
