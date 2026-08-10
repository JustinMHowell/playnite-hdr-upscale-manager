using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.UiTests
{
    /* fullscreenConfig.json's FilterSettings (a separate file/property from Desktop's own
       FilterSettings in config.json) persists across app restarts — Playnite rewrites this file
       itself whenever a filter changes live in the UI, including the Aniki-ReMake theme's own
       default "2 player" feature-tag filter. Left in place, it silently excludes any game not
       tagged with it from the grid — including from the default, unfiltered view, not just
       search results — with no visible error. Reset before every Fullscreen UI test run for a
       known-clean starting grid, same pattern as PlayniteConfigAccessibility. */
    internal static class PlayniteFullscreenFilter
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Playnite", "fullscreenConfig.json");

        public static JToken Reset()
        {
            var json = JObject.Parse(File.ReadAllText(ConfigPath));
            var previous = json["FilterSettings"]?.DeepClone();
            json["FilterSettings"] = new JObject();
            File.WriteAllText(ConfigPath, json.ToString());
            return previous;
        }

        public static void Restore(JToken previousValue)
        {
            var json = JObject.Parse(File.ReadAllText(ConfigPath));
            if (previousValue == null)
            {
                json.Remove("FilterSettings");
            }
            else
            {
                json["FilterSettings"] = previousValue;
            }
            File.WriteAllText(ConfigPath, json.ToString());
        }
    }
}
