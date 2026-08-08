using System;
using System.IO;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Local-only smoke test for the OTHER manifest-refresh path: OnApplicationStarted
       (HdrUpscaleManagerPlugin.cs) fetching the manifest automatically on every Playnite
       launch, with no menu click involved at all — the counterpart to
       ManifestSyncUiTests' button-click path.

       Deliberately does NOT share a PlayniteFixture/IClassFixture with ManifestSyncUiTests:
       that test relies on deleting the file AFTER launch (its own comment explains the false
       positive that ordering avoids), which is the opposite of what this test needs — here
       the file has to be gone BEFORE Playnite launches, so the startup fetch is the only
       thing that can put it back. This test manages its own PlayniteFixture instance end to
       end (via PlayniteFixture.LaunchFresh) instead, so the two tests never share process
       state and can't affect each other's result either way. */
    [Collection(PlayniteUiCollection.Name)]
    public class ManifestAutoDownloadUiTests
    {
        private readonly ITestOutputHelper output;

        public ManifestAutoDownloadUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void OnPlayniteLaunch_WithNoLocalManifest_DownloadsManifestAutomatically()
        {
            var manifestPath = PluginPaths.ManifestPath;

            using (var fixture = PlayniteFixture.LaunchFresh(beforeLaunch: () =>
            {
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }))
            {
                // Must come BEFORE the file-wait below, not after: GetMainWindow() is what
                // dismisses Playnite's own "Startup Error — start in safe mode?" crash-recovery
                // dialog (see PlayniteFixture), which blocks addons from loading at all — no
                // dialog dismissal means OnApplicationStarted never fires and the file-wait
                // would just time out for a reason that has nothing to do with the manifest
                // fetch itself. That dialog is a real possibility on the very first launch
                // this test drives, since a prior test/run ending in anything other than a
                // clean WM_CLOSE flags the next launch as "closed unexpectedly."
                fixture.GetMainWindow();

                // By the time the main window is up, OnApplicationStarted's fetch has already
                // started (it runs on Playnite's own startup sequence, not after the window
                // renders) — this only needs to cover the fetch's own network latency, not
                // Playnite's full startup on top of it.
                var downloaded = Retry.WhileFalse(
                    () => File.Exists(manifestPath),
                    timeout: TimeSpan.FromSeconds(30),
                    interval: TimeSpan.FromMilliseconds(500)).Result;

                if (!downloaded)
                {
                    output.WriteLine($"manifest.json path checked: {manifestPath}");
                    output.WriteLine($"Plugin log directory: {Path.Combine(PluginPaths.DataDir, "logs")}");
                }

                Assert.True(downloaded,
                    "manifest.json was not recreated within 30s of Playnite's main window " +
                    "becoming ready (with no local manifest present) — OnApplicationStarted's " +
                    "automatic fetch either didn't run or failed. Check the plugin's own log " +
                    "for the real reason.");
            }
        }
    }
}
