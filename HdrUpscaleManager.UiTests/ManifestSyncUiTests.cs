using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
    public class ManifestSyncUiTests : IClassFixture<PlayniteFixture>
    {
        // Playnite's own Id for this plugin (HdrUpscaleManagerPlugin.Id) — the folder Playnite
        // stores per-plugin data under is named after it.
        private const string PluginId = "5C15D52C-22E9-4668-881D-C5DAF69A2F0E";

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
            var manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite", "ExtensionsData", PluginId, "manifest.json");

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
                (no Name/AutomationId at all, so it's found by position, not text)
             2. the main menu opens as a popup; click "Extensions"
             3. its submenu contains "HDR & Upscale Manager"; click it
             4. its submenu contains "Check manifest sync"; click it. */
        private void ClickCheckManifestSync(Window mainWindow)
        {
            var trigger = FindMenuTriggerButton(mainWindow);
            if (trigger == null)
            {
                Dump("main window", mainWindow);
                throw new InvalidOperationException(
                    "Couldn't find the top-left menu trigger icon by position. See the dumped " +
                    "tree above — if the sidebar layout changed, adjust FindMenuTriggerButton's " +
                    "position window.");
            }
            Mouse.Click(trigger.GetClickablePoint(), MouseButton.Left);

            var desktop = fixture.Automation.GetDesktop();
            ClickMenuItemByName(desktop, "Extensions", "the main menu");
            ClickMenuItemByName(desktop, "HDR & Upscale Manager", "the Extensions submenu");
            ClickMenuItemByName(desktop, "Check manifest sync", "the HDR & Upscale Manager submenu");
        }

        /* Confirmed straight from Stardust 2.0's own source (Views/Sidebar.xaml):
             <Border x:Name="PART_ElemMainMenu" Width="36" Height="36" Margin="2,10,2,25"
                     DockPanel.Dock="Top" ... />
           It's a Border (NOT a Button — searching ControlType.Button was the earlier bug,
           it silently excluded this element entirely), docked at the very top of the sidebar,
           above the row of nav-icon Buttons found lower down. No x:Name→AutomationId mapping
           happens in WPF and no AutomationProperties.AutomationId is set here, so this can only
           be found by position/size — but now with the real numbers instead of a guess. */
        private static AutomationElement FindMenuTriggerButton(Window mainWindow)
        {
            var winRect = mainWindow.BoundingRectangle;
            return mainWindow.FindAllDescendants()
                .Select(b => new { Element = b, Rect = SafeRect(b) })
                .Where(x => x.Rect.HasValue)
                .Select(x => new { x.Element, Rect = x.Rect.Value })
                .Where(x =>
                {
                    // Percentages of the window's own size, not fixed pixels, so this doesn't
                    // break at a different resolution/DPI/window size. Source says 36x36 at
                    // margin-top 10 — window a bit generous around that since the sidebar's own
                    // top offset (below any window chrome) isn't guaranteed to be exactly 0.
                    var relXPct = (x.Rect.X - winRect.X) / (double)winRect.Width;
                    var relYPct = (x.Rect.Y - winRect.Y) / (double)winRect.Height;
                    var widthPct = x.Rect.Width / (double)winRect.Width;
                    var heightPct = x.Rect.Height / (double)winRect.Height;
                    return relXPct >= 0 && relXPct <= 0.02 && relYPct >= 0 && relYPct <= 0.10 &&
                           widthPct >= 0.02 && widthPct <= 0.05 && heightPct >= 0.02 && heightPct <= 0.08 &&
                           IsActuallyVisible(x.Element, x.Rect);
                })
                .OrderBy(x => x.Rect.Y)
                .Select(x => x.Element)
                .FirstOrDefault();
        }

        private static System.Drawing.Rectangle? SafeRect(AutomationElement element)
        {
            try { return element.BoundingRectangle; }
            catch { return null; }
        }

        /* Regression guard for a real false-positive caught 2026-08-08: Playnite (this theme,
           at least) keeps menu elements in the automation tree even while collapsed/closed, so
           FindFirstDescendant happily "finds" them and GetClickablePoint()/Mouse.Click()
           "succeed" with no exception — while nothing visible on screen ever gets clicked.
           A collapsed/closed element reports IsOffscreen=true and/or a zero-size bounding rect;
           insist on neither before trusting a match enough to click it. */
        private static bool IsActuallyVisible(AutomationElement element, System.Drawing.Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            try { if (element.Properties.IsOffscreen.ValueOrDefault) return false; }
            catch { /* property not supported on this control type — rect check above still applies */ }
            return true;
        }

        private void ClickMenuItemByName(AutomationElement desktop, string name, string whereDescription)
        {
            var item = Retry.WhileNull(
                () =>
                {
                    var found = desktop.FindFirstDescendant(cf => cf.ByName(name));
                    if (found == null) return null;
                    var rect = SafeRect(found);
                    return rect.HasValue && IsActuallyVisible(found, rect.Value) ? found : null;
                },
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (item == null)
            {
                Dump($"desktop (looking for a visible '{name}' in {whereDescription})", desktop);
                throw new InvalidOperationException(
                    $"Couldn't find a visible '{name}' in {whereDescription} (it may exist in " +
                    "the tree but collapsed/off-screen, which no longer counts — see " +
                    "IsActuallyVisible). See the dumped tree above to fix the selector.");
            }

            Mouse.Click(item.GetClickablePoint(), MouseButton.Left);
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

        private void Dump(string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }
    }
}
