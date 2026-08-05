using System;
using System.Collections.Generic;
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
