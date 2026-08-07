using System;
using System.Text;
using FlaUI.Core.AutomationElements;

namespace HdrUpscaleManager.UiTests
{
    /* Playnite's own automation tree isn't documented anywhere this repo can reference, so the
       selectors in ManagerWindowUiTests are best-effort name matches. When one stops matching
       (a Playnite update, a different theme, etc.), this dump is what you paste back in to see
       the real tree and fix the one selector that broke — not a general-purpose utility. */
    internal static class AutomationTreeDumper
    {
        public static string Dump(AutomationElement root, int maxDepth = 6, int maxChildrenPerNode = 40)
        {
            var sb = new StringBuilder();
            Walk(root, 0, maxDepth, maxChildrenPerNode, sb);
            return sb.ToString();
        }

        private static void Walk(AutomationElement element, int depth, int maxDepth, int maxChildrenPerNode, StringBuilder sb)
        {
            if (element == null || depth > maxDepth) return;

            // Each property read independently — some control types don't support a given
            // property (e.g. AutomationId throwing "not supported" for certain elements) and
            // that shouldn't blank out the whole node or abort recursing into its children.
            var name = SafeRead(() => element.Name, "<name unsupported>");
            var controlType = SafeRead(() => element.ControlType.ToString(), "<controltype unsupported>");
            var automationId = SafeRead(() => element.AutomationId, "<automationid unsupported>");

            sb.AppendLine($"{new string(' ', depth * 2)}[{controlType}] Name=\"{name}\" AutomationId=\"{automationId}\"");

            AutomationElement[] children;
            try
            {
                children = element.FindAllChildren();
            }
            catch
            {
                return;
            }

            var count = 0;
            foreach (var child in children)
            {
                if (count++ >= maxChildrenPerNode)
                {
                    sb.AppendLine(new string(' ', (depth + 1) * 2) + $"... ({children.Length - maxChildrenPerNode} more children truncated)");
                    break;
                }
                Walk(child, depth + 1, maxDepth, maxChildrenPerNode, sb);
            }
        }

        private static string SafeRead(Func<string> read, string fallback)
        {
            try { return read(); }
            catch { return fallback; }
        }
    }
}
