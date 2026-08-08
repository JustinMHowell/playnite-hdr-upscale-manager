using System;
using System.IO;

namespace HdrUpscaleManager.UiTests
{
    /* Shared by every UI test that needs to reach into the plugin's own on-disk state under
       %AppData%\Playnite\ExtensionsData\{PluginId}\... — HdrUpscaleManagerPlugin.Id duplicated
       here since this project deliberately has no reference back to the addon (see README.md). */
    internal static class PluginPaths
    {
        public const string PluginId = "5C15D52C-22E9-4668-881D-C5DAF69A2F0E";

        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Playnite", "ExtensionsData", PluginId);

        public static string ManifestPath => Path.Combine(DataDir, "manifest.json");

        public static string RecordStorePath(string fileName) => Path.Combine(DataDir, fileName);

        public static string CacheFilePath(string cacheDirName, string fileName) =>
            Path.Combine(DataDir, cacheDirName, fileName);
    }
}
