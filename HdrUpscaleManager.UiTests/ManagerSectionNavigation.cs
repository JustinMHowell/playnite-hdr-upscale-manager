using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Clicks a control in the manager window (UI/ManagerView.xaml) by its AutomationId — every
       interactive control there sets AutomationProperties.AutomationId explicitly for exactly
       this reason.

       This used to scope lookups by section-heading text instead (first by "the heading's
       immediate parent element", then by document order between one heading and the next). Both
       approaches broke in practice: confirmed live 2026-08-08 that Parent-based scoping resolved
       to an ancestor spanning every section, so FindSection("Luma") then ByName("Install") matched
       RenoDX's "Install" button instead, since RenoDX's own same-named button happened to come
       first in the tree either way. Document-order scoping fixed that specific bug but still
       depended on which section a button visually sits in and in what order — which the
       ReShade/HDR-toggle two-column merge (2026-08-09) would have broken again. AutomationId
       lookups have no such dependency: no scoping, no ambiguity between same-named buttons in
       different sections, and no sensitivity to layout changes. */
    internal static class ManagerSectionNavigation
    {
        public static void ClickButton(Window managerWindow, string automationId, ITestOutputHelper output)
        {
            var button = Retry.WhileNull(
                () => managerWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
                timeout: TimeSpan.FromSeconds(10)).Result;

            if (button == null)
            {
                output.WriteLine($"--- automation tree dump: manager window (looking for AutomationId '{automationId}') ---");
                output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                throw new InvalidOperationException(
                    $"Couldn't find a control with AutomationId '{automationId}'. " +
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
    }
}
