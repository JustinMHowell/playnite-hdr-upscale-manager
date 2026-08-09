using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Regression test for a real bug caught live 2026-08-09: the addon's About/settings page
       (Plugin/HdrUpscaleManagerSettingsView.xaml) never showed up anywhere in Playnite at all.
       Root cause — Playnite only lists a GenericPlugin under Add-ons > Extension Settings when
       Properties.HasSettings is explicitly set true (see AddonsViewModel.cs in Playnite's own
       source: GenericPlugins is filtered on exactly that flag); HdrUpscaleManagerPlugin's
       constructor set GetSettings/GetSettingsView but never set Properties itself. Confirmed via
       Playnite's own source that the real path is: main menu -> "Add-ons" -> the left tree's
       "Extension Settings" section -> "Generic" sub-list -> "HDR & Upscale Manager" -> its
       settings view renders on the right of the SAME window (not a separate popup). */
    [Collection(PlayniteUiCollection.Name)]
    public class AddonsSettingsUiTests : IClassFixture<PlayniteFixture>
    {
        private readonly PlayniteFixture fixture;
        private readonly ITestOutputHelper output;

        public AddonsSettingsUiTests(PlayniteFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        [Fact]
        public void OpenAddonsWindow_SelectHdrUpscaleManager_ShowsAboutPageContent()
        {
            var mainWindow = fixture.GetMainWindow();
            var desktop = fixture.Automation.GetDesktop();

            // Retries the whole open-menu-then-click sequence, not just the final wait — a
            // popup menu opening is itself a race (same WPF CommandManager/input-timing class of
            // flake ClickInstallUntilRecordAppears etc. already retry around elsewhere in this
            // suite), confirmed live 2026-08-09: the exact same selectors succeeded on some runs
            // and silently found nothing on others with no code change in between.
            Window addonsWindow = null;
            const int maxMenuAttempts = 3;
            for (var attempt = 1; attempt <= maxMenuAttempts && addonsWindow == null; attempt++)
            {
                MainMenuNavigation.OpenMainMenu(mainWindow, output);
                // Playnite's real menu text is "Add-ons…" (LOCMenuAddons in LocSource.xaml) —
                // with a trailing ellipsis character, not three dots — confirmed live 2026-08-09
                // after a plain "Add-ons" exact-match lookup failed to find it.
                MainMenuNavigation.ClickMenuItemByName(desktop, "Add-ons…", "the main menu", output);

                // Playnite's own source (AddonsWindow.xaml) sets AutomationProperties.AutomationId
                // ="WindowExtensions" on this window — a real, reliable identifier, unlike its
                // Title (bound to the same LOCMenuAddons key as the menu item that opens it, i.e.
                // also "Add-ons…", confirmed live 2026-08-09 after a Name-based lookup for
                // "Add-ons" never matched it).
                addonsWindow = Retry.WhileNull(
                    () => desktop.FindFirstDescendant(cf => cf.ByAutomationId("WindowExtensions"))?.AsWindow(),
                    timeout: TimeSpan.FromSeconds(8)).Result;

                if (addonsWindow == null)
                {
                    output.WriteLine($"The Add-ons window didn't appear after menu attempt {attempt}/{maxMenuAttempts} — retrying.");
                }
            }

            if (addonsWindow == null)
            {
                Dump("desktop (waiting for the Add-ons window)", desktop);
                throw new InvalidOperationException(
                    $"The Add-ons window never appeared after {maxMenuAttempts} attempts at opening the main menu and clicking 'Add-ons…'.");
            }

            WindowStateHelper.Maximize(addonsWindow, output);

            try
            {
                // The "Generic" node under "Extensions settings" (AddonsWindow.xaml) has no
                // IsExpanded="True" of its own, unlike its "Extensions settings" parent — WPF's
                // TreeView doesn't create its children's automation elements at all until it's
                // actually expanded, confirmed live 2026-08-09 after a plain FindFirstDescendant
                // for "HDR & Upscale Manager" kept finding nothing with zero children under
                // "Generic" in the dump. There are three "Generic" TreeItems in this window (the
                // other two are under "Installed" and "Browse", both empty Tag-based placeholders)
                // — the first one in document order is the one bound to GenericPlugins.
                var genericNode = Retry.WhileNull(
                    () => addonsWindow.FindFirstDescendant(cf => cf.ByName("Generic"))?.AsTreeItem(),
                    timeout: TimeSpan.FromSeconds(10)).Result;

                if (genericNode == null)
                {
                    Dump("Add-ons window (looking for the 'Generic' extension-settings tree node)", addonsWindow);
                    throw new InvalidOperationException("Couldn't find the 'Generic' tree node under Add-ons' Extension Settings section.");
                }

                genericNode.Expand();

                var pluginEntry = Retry.WhileNull(
                    () => addonsWindow.FindFirstDescendant(cf => cf.ByName("HDR & Upscale Manager")),
                    timeout: TimeSpan.FromSeconds(10)).Result;

                if (pluginEntry == null)
                {
                    Dump("Add-ons window (looking for 'HDR & Upscale Manager' in the Generic Extension Settings list)", addonsWindow);
                    throw new InvalidOperationException(
                        "Couldn't find 'HDR & Upscale Manager' under Add-ons' Extension Settings list — " +
                        "this is exactly the bug this test guards against (Properties.HasSettings not set).");
                }

                Mouse.Click(pluginEntry.GetClickablePoint(), MouseButton.Left);

                // AuthorLine in HdrUpscaleManagerSettings.cs ("By Matheus Faustino") — content
                // only the About page shows, not the plugin's name (which also appears in the
                // left-hand tree item itself).
                var authorText = Retry.WhileNull(
                    () => addonsWindow.FindFirstDescendant(cf => cf.ByName("By Matheus Faustino")),
                    timeout: TimeSpan.FromSeconds(10)).Result;

                if (authorText == null)
                {
                    Dump("Add-ons window (after selecting 'HDR & Upscale Manager')", addonsWindow);
                }

                Assert.NotNull(authorText);
            }
            finally
            {
                try { addonsWindow.Close(); } catch (Exception ex) { output.WriteLine($"addonsWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }

        private void Dump(string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }
    }
}
