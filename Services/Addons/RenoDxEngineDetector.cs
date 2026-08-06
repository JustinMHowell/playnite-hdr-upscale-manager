using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class RenoDxEngineDetectionResult
    {
        public string InstallPath { get; set; }
        public RenoDxEngine? Engine { get; set; }
    }

    /* Scoped-down port of RHI's GameDetectionService.DetectEngineAndPath — covers only the two
       engines the generic RenoDX addon supports (Unreal, Unity); RE Engine/UE3-legacy detection
       is out of scope here since RenoDX only targets UE4+/Unity anyway. Also applies the
       manifest's per-game overrides (installPathOverrides / engineHintOverrides), used by RHI
       for Game Pass and other installs where auto-detection can't run or gets it wrong. */
    public static class RenoDxEngineDetector
    {
        private const int MaxScanDepth = 3;

        public static RenoDxEngineDetectionResult Detect(
            string rootPath,
            string gameName,
            IDictionary<string, string> installPathOverrides = null,
            IDictionary<string, string> engineHintOverrides = null)
        {
            var effectiveRoot = rootPath;
            if (gameName != null && installPathOverrides != null &&
                installPathOverrides.TryGetValue(gameName, out var subPath) && !string.IsNullOrEmpty(subPath))
            {
                var overridden = Path.Combine(rootPath, subPath);
                if (Directory.Exists(overridden)) effectiveRoot = overridden;
            }

            if (gameName != null && engineHintOverrides != null &&
                engineHintOverrides.TryGetValue(gameName, out var hint))
            {
                var hinted = ParseEngineHint(hint);
                if (hinted.HasValue)
                {
                    return new RenoDxEngineDetectionResult
                    {
                        InstallPath = FindEngineFolder(effectiveRoot, hinted.Value) ?? effectiveRoot,
                        Engine = hinted
                    };
                }
            }

            var uePath = FindUnrealBinariesFolder(effectiveRoot);
            if (uePath != null)
                return new RenoDxEngineDetectionResult { InstallPath = uePath, Engine = RenoDxEngine.UnrealEngine };

            var unityDir = FindUnityPlayerDirectory(effectiveRoot);
            if (unityDir != null)
                return new RenoDxEngineDetectionResult { InstallPath = unityDir, Engine = RenoDxEngine.UnityEngine };

            return new RenoDxEngineDetectionResult { InstallPath = effectiveRoot, Engine = null };
        }

        private static string FindEngineFolder(string root, RenoDxEngine engine) =>
            engine == RenoDxEngine.UnrealEngine ? FindUnrealBinariesFolder(root) : FindUnityPlayerDirectory(root);

        private static RenoDxEngine? ParseEngineHint(string hint)
        {
            if (string.IsNullOrEmpty(hint)) return null;
            if (hint.IndexOf("unreal", StringComparison.OrdinalIgnoreCase) >= 0) return RenoDxEngine.UnrealEngine;
            if (hint.IndexOf("unity", StringComparison.OrdinalIgnoreCase) >= 0) return RenoDxEngine.UnityEngine;
            return null;
        }

        /* Unreal's addon must go in Binaries\Win64 (or WinGDK), never inside an Engine\
           subfolder — that copy is the shared editor/engine binaries, not the game's own. */
        private static string FindUnrealBinariesFolder(string root)
        {
            foreach (var binariesDir in EnumerateDirectoriesNamed(root, "Binaries", MaxScanDepth))
            {
                if (binariesDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(seg => string.Equals(seg, "Engine", StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var name in new[] { "Win64", "WinGDK" })
                {
                    var candidate = Path.Combine(binariesDir, name);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        private static string FindUnityPlayerDirectory(string root)
        {
            var file = FindFileShallow(root, "UnityPlayer.dll", MaxScanDepth);
            return file != null ? Path.GetDirectoryName(file) : null;
        }

        private static IEnumerable<string> EnumerateDirectoriesNamed(string root, string name, int maxDepth)
        {
            IEnumerable<string> Walk(string dir, int depth)
            {
                if (depth > maxDepth) yield break;

                IEnumerable<string> subDirs;
                try { subDirs = Directory.EnumerateDirectories(dir); }
                catch { yield break; }

                foreach (var sub in subDirs)
                {
                    if (string.Equals(Path.GetFileName(sub), name, StringComparison.OrdinalIgnoreCase))
                        yield return sub;

                    foreach (var nested in Walk(sub, depth + 1))
                        yield return nested;
                }
            }

            return Walk(root, 1);
        }

        private static string FindFileShallow(string root, string fileName, int maxDepth)
        {
            string Walk(string dir, int depth)
            {
                if (depth > maxDepth) return null;

                try
                {
                    var candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate)) return candidate;

                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var found = Walk(sub, depth + 1);
                        if (found != null) return found;
                    }
                }
                catch { /* inaccessible folder — skip it */ }

                return null;
            }

            return Walk(root, 1);
        }
    }
}
