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

            Mouse.Click(button.GetClickablePoint(), MouseButton.Left);
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
