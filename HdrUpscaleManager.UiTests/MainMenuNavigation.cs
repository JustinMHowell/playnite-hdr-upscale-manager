using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Opens Playnite's own main hamburger menu and clicks through named items in it — shared by
       ManifestSyncUiTests (Extensions -> HDR & Upscale Manager -> Check manifest sync) and
       AddonsSettingsUiTests (Add-ons -> the Generic Extension Settings list). Confirmed live
       2026-08-08 (Stardust 2.0 theme) via screenshots + reading Playnite's own source
       (MainMenu.cs: MenuSection.StartsWith("@") nests under the Extensions menu item, it's not
       its own flat top-level entry). */
    internal static class MainMenuNavigation
    {
        public static void OpenMainMenu(Window mainWindow, ITestOutputHelper output)
        {
            // A click at the trigger's screen coordinates only reaches Playnite if its window is
            // actually the foreground one there — with other apps open (browser, editor) on the
            // same desktop, Playnite can sit unfocused behind/under them, and the popup this
            // opens either never appears or closes immediately once clicked without focus.
            try { mainWindow.SetForeground(); } catch (Exception ex) { output.WriteLine($"mainWindow.SetForeground() failed: {ex.Message}"); }

            var trigger = FindMenuTriggerButton(mainWindow);
            if (trigger == null)
            {
                Dump(output, "main window", mainWindow);
                throw new InvalidOperationException(
                    "Couldn't find the top-left menu trigger icon by position. See the dumped " +
                    "tree above — if the sidebar layout changed, adjust FindMenuTriggerButton's " +
                    "position window.");
            }
            Mouse.Click(trigger.GetClickablePoint(), MouseButton.Left);

            // The popup's own open animation isn't instant — a lookup fired immediately after
            // the click can find the popup's elements before they've settled into their final
            // position (or before the popup has fully opened at all), so a click on a "found"
            // item can land on empty space mid-animation. Confirmed live 2026-08-09: identical
            // selectors succeeded on some runs and silently found nothing on others with no code
            // change in between — exactly the symptom of a race against this animation.
            System.Threading.Thread.Sleep(300);
        }

        public static void ClickMenuItemByName(AutomationElement desktop, string name, string whereDescription, ITestOutputHelper output)
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
                Dump(output, $"desktop (looking for a visible '{name}' in {whereDescription})", desktop);
                throw new InvalidOperationException(
                    $"Couldn't find a visible '{name}' in {whereDescription} (it may exist in " +
                    "the tree but collapsed/off-screen, which no longer counts — see " +
                    "IsActuallyVisible). See the dumped tree above to fix the selector.");
            }

            // Same settle reasoning as OpenMainMenu's own delay — re-read the clickable point
            // fresh after the pause rather than trusting the one captured the instant this
            // element was first found, in case the popup was still animating into position.
            System.Threading.Thread.Sleep(150);

            Mouse.Click(item.GetClickablePoint(), MouseButton.Left);
        }

        /* Confirmed straight from Stardust 2.0's own source (Views/Sidebar.xaml):
             <Border x:Name="PART_ElemMainMenu" Width="36" Height="36" Margin="2,10,2,25"
                     DockPanel.Dock="Top" ... />
           It's a Border (NOT a Button — searching ControlType.Button was the earlier bug, it
           silently excluded this element entirely), docked at the very top of the sidebar, above
           the row of nav-icon Buttons found lower down. No x:Name→AutomationId mapping happens in
           WPF and no AutomationProperties.AutomationId is set here, so this can only be found by
           position/size — but now with the real numbers instead of a guess. */
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
                    // break at a different resolution/DPI/window size. Widened 2026-08-09 after
                    // a real screenshot showed the actual icon is smaller relative to the window
                    // than the original 0.02-0.05 width/height band assumed — that band excluded
                    // the real trigger and let some other small element in the library grid match
                    // instead, sending clicks into the game covers rather than the menu.
                    var relXPct = (x.Rect.X - winRect.X) / (double)winRect.Width;
                    var relYPct = (x.Rect.Y - winRect.Y) / (double)winRect.Height;
                    var widthPct = x.Rect.Width / (double)winRect.Width;
                    var heightPct = x.Rect.Height / (double)winRect.Height;
                    return relXPct >= 0 && relXPct <= 0.025 && relYPct >= 0 && relYPct <= 0.10 &&
                           widthPct >= 0.005 && widthPct <= 0.06 && heightPct >= 0.005 && heightPct <= 0.08 &&
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

        /* Regression guard for a real false-positive caught 2026-08-08: Playnite (this theme, at
           least) keeps menu elements in the automation tree even while collapsed/closed, so
           FindFirstDescendant happily "finds" them and GetClickablePoint()/Mouse.Click()
           "succeed" with no exception — while nothing visible on screen ever gets clicked. A
           collapsed/closed element reports IsOffscreen=true and/or a zero-size bounding rect;
           insist on neither before trusting a match enough to click it. */
        private static bool IsActuallyVisible(AutomationElement element, System.Drawing.Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            try { if (element.Properties.IsOffscreen.ValueOrDefault) return false; }
            catch { /* property not supported on this control type — rect check above still applies */ }
            return true;
        }

        private static void Dump(ITestOutputHelper output, string label, AutomationElement root)
        {
            output.WriteLine($"--- automation tree dump: {label} ---");
            output.WriteLine(AutomationTreeDumper.Dump(root));
        }
    }
}
