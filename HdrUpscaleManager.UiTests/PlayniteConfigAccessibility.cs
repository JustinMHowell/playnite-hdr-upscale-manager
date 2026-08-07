using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.UiTests
{
    /* Playnite deliberately ships with UI Automation disabled by default — confirmed straight
       from its own source (WindowBase.cs, OnCreateAutomationPeer): every window returns a
       hardcoded-empty automation peer unless config.json's AccessibilityInterface is 1
       (AlwaysOn), or it's 0 (Auto) *and* a real screen reader process (narrator/nvda/jaws) is
       actually running. That property also carries [RequiresRestart] — Playnite reads it once
       at startup, so this only takes effect on a fresh launch, not a running instance.

       This patches config.json directly rather than going through Playnite's own settings UI,
       since there's no other way to flip it from a test. It touches your real Playnite config
       file — see the restore step below and the warning in README.md. */
    internal static class PlayniteConfigAccessibility
    {
        private const string PropertyName = "AccessibilityInterface";
        private const int AlwaysOn = 1;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Playnite", "config.json");

        /// <summary>
        /// Forces AccessibilityInterface to AlwaysOn, returning whatever the field's raw JSON
        /// value was before (to be restored later — null if the key wasn't present at all, in
        /// which case restoring means removing it again to fall back to Playnite's own default).
        /// </summary>
        public static JToken ForceAlwaysOn()
        {
            var json = JObject.Parse(File.ReadAllText(ConfigPath));
            var previous = json[PropertyName]?.DeepClone();
            json[PropertyName] = AlwaysOn;
            File.WriteAllText(ConfigPath, json.ToString());
            return previous;
        }

        public static void Restore(JToken previousValue)
        {
            var json = JObject.Parse(File.ReadAllText(ConfigPath));
            if (previousValue == null)
            {
                json.Remove(PropertyName);
            }
            else
            {
                json[PropertyName] = previousValue;
            }
            File.WriteAllText(ConfigPath, json.ToString());
        }
    }
}
