using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Clicks a button inside one named component section of the manager window (UI/ManagerView.xaml
       — ReShade/RenoDX/Luma/DXVK/OptiScaler/HDR toggle, each a Border > StackPanel with a title
       TextBlock followed by its own buttons).

       Scoping by "the section heading's immediate parent element" (what RenoDxInstallUiTests
       originally did) doesn't work: confirmed live 2026-08-08 that a section heading's Parent
       resolves to an ancestor spanning every section, not just the one under that heading —
       FindSection("Luma") then FindFirstDescendant(ByName("Install")) inside it matched RenoDX's
       "Install" button instead, since RenoDX is the first section in the window with a plainly-
       named "Install" button and both headings' Parent walked up to the same over-broad
       container. That bug was invisible on RenoDX itself only because RenoDX's own "Install" is
       coincidentally the very first "Install"-named button in the whole window either way.

       Scoping by document order instead — between this section's heading and the next known
       section heading — doesn't depend on Border/StackPanel appearing as distinct automation
       elements at all, so it doesn't have the same failure mode. */
    internal static class ManagerSectionNavigation
    {
        private static readonly string[] SectionTitles =
        {
            "ReShade", "RenoDX", "Luma", "DXVK+Lilium HDR", "OptiScaler (AMD)", "OS HDR toggle on launch/exit",
        };

        public static void ClickSectionButton(Window managerWindow, string sectionTitle, string buttonName, ITestOutputHelper output)
        {
            var button = Retry.WhileNull(
                () => FindButton(managerWindow, sectionTitle, buttonName),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (button == null)
            {
                output.WriteLine($"--- automation tree dump: manager window (looking for '{sectionTitle}' -> '{buttonName}') ---");
                output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                throw new InvalidOperationException(
                    $"Couldn't find the '{buttonName}' button under the '{sectionTitle}' section. " +
                    "See the dumped tree above to fix the lookup.");
            }

            ScrollIntoView(managerWindow, button);
            Mouse.Click(button.GetClickablePoint(), MouseButton.Left);
        }

        /* The manager window's sections sit inside a ScrollViewer (UI/ManagerView.xaml) — a
           section far enough down the list (OptiScaler, the OS HDR toggle) can be scrolled out
           of the visible viewport even though FindAllDescendants still enumerates it (WPF keeps
           off-screen elements in the automation tree, confirmed live 2026-08-08 against
           OptiScaler's "Install" button). GetClickablePoint() on a clipped-out element still
           returns a coordinate, but it can land outside the window entirely or on whatever's
           underneath, so the click never reaches the real button. Scrolling the mouse wheel over
           the window's center brings the ScrollViewer's content up until the target's own
           bounding rectangle sits fully inside the window's. */
        private static void ScrollIntoView(Window managerWindow, AutomationElement target)
        {
            var windowRect = managerWindow.BoundingRectangle;
            Mouse.Position = new System.Drawing.Point(
                windowRect.X + windowRect.Width / 2,
                windowRect.Y + windowRect.Height / 2);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                var rect = target.BoundingRectangle;
                if (rect.Height > 0 && windowRect.Contains(rect)) return;
                Mouse.Scroll(-3);
            }
        }

        private static AutomationElement FindButton(Window managerWindow, string sectionTitle, string buttonName)
        {
            var all = managerWindow.FindAllDescendants();
            var headingIndex = Array.FindIndex(all, e => e.Name == sectionTitle);
            if (headingIndex < 0) return null;

            var nextHeadingIndex = all.Length;
            for (var i = headingIndex + 1; i < all.Length; i++)
            {
                if (all[i].Name != sectionTitle && SectionTitles.Contains(all[i].Name))
                {
                    nextHeadingIndex = i;
                    break;
                }
            }

            for (var i = headingIndex + 1; i < nextHeadingIndex; i++)
            {
                if (all[i].ControlType == ControlType.Button && all[i].Name == buttonName)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
