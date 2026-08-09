using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Local-only smoke test for the "Check manifest sync" main-menu action — navigates
       Playnite's real top-level menu bar (no game required, unlike ManagerWindowUiTests),
       clicks through to the addon's own menu item, and confirms the click actually did
       something by checking the manifest.json file on disk got a fresh write, not just that
       some dialog appeared. See README.md in this folder for setup. */
    [Collection(PlayniteUiCollection.Name)]
    public class ManifestSyncUiTests : IClassFixture<PlayniteFixture>
    {
        private readonly PlayniteFixture fixture;
        private readonly ITestOutputHelper output;

        public ManifestSyncUiTests(PlayniteFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        [Fact]
        public void CheckManifestSync_ClickedFromMainMenu_RefreshesManifestFileOnDisk()
        {
            var manifestPath = PluginPaths.ManifestPath;

            var mainWindow = fixture.GetMainWindow();

            // Delete only after the window is ready, not before launch: OnApplicationStarted
            // (HdrUpscaleManagerPlugin.cs) already refreshes the manifest once on every launch,
            // and that fetch has certainly finished by the time the window is fully loaded. If
            // we deleted before launch instead, that startup refresh would recreate the file on
            // its own and the test would pass even if the click chain did nothing — exactly the
            // false positive caught 2026-08-08 (test passed while no menu visibly opened).
            // Deleting here means any reappearance from this point on can only be the click.
            File.Delete(manifestPath);

            ClickCheckManifestSync(mainWindow);

            // The fetch is async (network round-trip), so poll rather than assume it's done
            // the instant the click returns.
            var refreshed = Retry.WhileFalse(
                () => File.Exists(manifestPath),
                timeout: TimeSpan.FromSeconds(20),
                interval: TimeSpan.FromMilliseconds(250)).Result;

            if (!refreshed)
            {
                output.WriteLine($"manifest.json path checked: {manifestPath}");
                output.WriteLine("Still doesn't exist — the click chain didn't reach the action.");
            }

            Assert.True(refreshed,
                "manifest.json was not recreated within 20s of clicking 'Check manifest sync' " +
                "(it was deleted right before clicking) — either the click didn't reach the " +
                "action, or the fetch failed (no network would show as an error dialog in " +
                "Playnite, not a silent no-op).");

            CloseResultDialogIfPresent();
        }

        /* Confirmed live (2026-08-08, Stardust 2.0 theme) via screenshots + reading Playnite's
           own source (MainMenu.cs: MenuSection.StartsWith("@") nests under the Extensions menu
           item, it's not its own flat top-level entry): the real path is —
             1. click the icon-only trigger button in the top-left vertical sidebar strip
                (no Name/AutomationId at all, so it's found by position, not text — see
                MainMenuNavigation, shared with AddonsSettingsUiTests)
             2. the main menu opens as a popup; click "Extensions"
             3. its submenu contains "HDR & Upscale Manager"; click it
             4. its submenu contains "Check manifest sync"; click it. */
        private void ClickCheckManifestSync(Window mainWindow)
        {
            MainMenuNavigation.OpenMainMenu(mainWindow, output);

            var desktop = fixture.Automation.GetDesktop();
            MainMenuNavigation.ClickMenuItemByName(desktop, "Extensions", "the main menu", output);
            MainMenuNavigation.ClickMenuItemByName(desktop, "HDR & Upscale Manager", "the Extensions submenu", output);
            MainMenuNavigation.ClickMenuItemByName(desktop, "Check manifest sync", "the HDR & Upscale Manager submenu", output);
        }

        private void CloseResultDialogIfPresent()
        {
            // Best-effort cleanup only — CheckManifestSyncAsync shows a ShowMessage/
            // ShowErrorMessage dialog on completion. Not asserted on: the file-write check
            // above is the real assertion, and Playnite's dialog chrome/button naming isn't
            // something worth hard-failing the test over if it doesn't match.
            try
            {
                var desktop = fixture.Automation.GetDesktop();
                var dialog = Retry.WhileNull(
                    () => desktop.FindFirstDescendant(cf => cf.ByName("HDR & Upscale Manager — Manifest Sync"))
                        ?? desktop.FindFirstDescendant(cf => cf.ByName("HDR & Upscale Manager")),
                    timeout: TimeSpan.FromSeconds(5)).Result;

                var okButton = dialog?.FindFirstDescendant(cf => cf.ByName("OK"));
                if (okButton != null)
                {
                    Mouse.Click(okButton.GetClickablePoint(), MouseButton.Left);
                }
            }
            catch
            {
                // Cleanup is not the assertion — swallow and let the user dismiss manually.
            }
        }
    }
}
