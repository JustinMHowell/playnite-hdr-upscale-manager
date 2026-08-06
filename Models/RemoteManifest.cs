using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HdrUpscaleManager.Models
{
    /* Schema observed in RHI's RemoteManifest, covering the fields used here for now.
       Unmodeled fields are safely ignored by Newtonsoft.Json rather than dropped or erroring. */
    public class RemoteManifest
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("blacklist")]
        public List<string> Blacklist { get; set; }

        [JsonProperty("nativeHdrGames")]
        public List<string> NativeHdrGames { get; set; }

        [JsonProperty("ueExtendedGames")]
        public List<string> UeExtendedGames { get; set; }

        [JsonProperty("noUeExtendedGames")]
        public List<string> NoUeExtendedGames { get; set; }

        [JsonProperty("lumaRenodxCompat")]
        public List<string> LumaRenodxCompat { get; set; }

        [JsonProperty("lumaDefaultGames")]
        public List<string> LumaDefaultGames { get; set; }

        [JsonProperty("wikiNameOverrides")]
        public Dictionary<string, string> WikiNameOverrides { get; set; }

        [JsonProperty("lumaNameOverrides")]
        public Dictionary<string, string> LumaNameOverrides { get; set; }

        [JsonProperty("installPathOverrides")]
        public Dictionary<string, string> InstallPathOverrides { get; set; }

        [JsonProperty("engineHintOverrides")]
        public Dictionary<string, string> EngineHintOverrides { get; set; }

        [JsonProperty("gameNotes")]
        public Dictionary<string, GameNoteEntry> GameNotes { get; set; }

        [JsonProperty("lumaGameNotes")]
        public Dictionary<string, GameNoteEntry> LumaGameNotes { get; set; }

        [JsonProperty("addonPacks")]
        public Dictionary<string, ManifestAddonPack> AddonPacks { get; set; }

        /* Component-level download URL overrides, keyed by a fixed component id (e.g.
           "ueExtended") rather than a game name. */
        [JsonProperty("componentUrls")]
        public Dictionary<string, string> ComponentUrls { get; set; }

        [JsonProperty("dxvkBlacklist")]
        public List<string> DxvkBlacklist { get; set; }

        /* Per-game forced graphics-API override (e.g. "D3D9"), used when PeInspector's
           auto-detection can't determine it. */
        [JsonProperty("dxvkApiOverrides")]
        public Dictionary<string, string> DxvkApiOverrides { get; set; }

        /* Per-game forced DLL-proxy name (e.g. "Mirror's Edge": "winmm.dll") — takes priority
           over the Vulkan auto-override. */
        [JsonProperty("optiScalerDllOverrides")]
        public Dictionary<string, string> OptiScalerDllOverrides { get; set; }

        /* Remaps a detected game name to the OptiScaler compatibility wiki's own naming when
           they differ. */
        [JsonProperty("optiScalerWikiNames")]
        public Dictionary<string, string> OptiScalerWikiNames { get; set; }

        public bool IsBlacklisted(string gameName)
        {
            return !string.IsNullOrEmpty(gameName) && Blacklist != null &&
                Blacklist.Contains(gameName, StringComparer.OrdinalIgnoreCase);
        }

        /* Luma and RenoDX are mutually exclusive per game by default — installing Luma normally
           means uninstalling RenoDX first — except for the manifest's curated allowlist of games
           known to run both safely at once. */
        public bool IsLumaRenodxCompatible(string gameName)
        {
            return !string.IsNullOrEmpty(gameName) && LumaRenodxCompat != null &&
                LumaRenodxCompat.Contains(gameName, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsDxvkBlacklisted(string gameName)
        {
            return !string.IsNullOrEmpty(gameName) && DxvkBlacklist != null &&
                DxvkBlacklist.Contains(gameName, StringComparer.OrdinalIgnoreCase);
        }

        /* Rebuilds every game-name-keyed dictionary with a case-insensitive comparer —
           Playnite's Game.Name won't always match the manifest's casing exactly. */
        public static void Normalize(RemoteManifest manifest)
        {
            if (manifest == null) return;

            manifest.WikiNameOverrides = CaseInsensitive(manifest.WikiNameOverrides);
            manifest.LumaNameOverrides = CaseInsensitive(manifest.LumaNameOverrides);
            manifest.InstallPathOverrides = CaseInsensitive(manifest.InstallPathOverrides);
            manifest.EngineHintOverrides = CaseInsensitive(manifest.EngineHintOverrides);
            manifest.GameNotes = CaseInsensitive(manifest.GameNotes);
            manifest.LumaGameNotes = CaseInsensitive(manifest.LumaGameNotes);
            manifest.DxvkApiOverrides = CaseInsensitive(manifest.DxvkApiOverrides);
            manifest.OptiScalerDllOverrides = CaseInsensitive(manifest.OptiScalerDllOverrides);
            manifest.OptiScalerWikiNames = CaseInsensitive(manifest.OptiScalerWikiNames);
        }

        private static Dictionary<string, T> CaseInsensitive<T>(Dictionary<string, T> source)
        {
            return source == null
                ? null
                : new Dictionary<string, T>(source, StringComparer.OrdinalIgnoreCase);
        }
    }

    public class GameNoteEntry
    {
        [JsonProperty("notes")]
        public string Notes { get; set; }

        [JsonProperty("notesUrl")]
        public string NotesUrl { get; set; }

        [JsonProperty("notesUrlLabel")]
        public string NotesUrlLabel { get; set; }
    }

    public class ManifestAddonPack
    {
        [JsonProperty("packageName")]
        public string PackageName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("downloadUrl32")]
        public string DownloadUrl32 { get; set; }

        [JsonProperty("downloadUrl64")]
        public string DownloadUrl64 { get; set; }

        [JsonProperty("repositoryUrl")]
        public string RepositoryUrl { get; set; }
    }
}
