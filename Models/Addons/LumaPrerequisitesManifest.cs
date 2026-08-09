using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HdrUpscaleManager.Models.Addons
{
    /* One prerequisite step a game needs before/alongside a Luma install — "type" is a fixed
       id ("dgvoodoo2" today) a step dispatches on; "reason" is shown to the user so an install
       action is never silent about why it's there. */
    public class LumaPrerequisiteStep
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    /* Our OWN self-maintained manifest of Luma install prerequisites, keyed by game display
       name — deliberately separate from manifest.json/RemoteManifest (that one's community data
       fetched from RankFTW/RHI's own repo; this one is ours to author and version). See
       LumaPrerequisitesManifestService for the fetch chain. */
    public class LumaPrerequisitesManifest
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("prerequisites")]
        public Dictionary<string, List<LumaPrerequisiteStep>> Prerequisites { get; set; } =
            new Dictionary<string, List<LumaPrerequisiteStep>>(StringComparer.OrdinalIgnoreCase);

        public List<LumaPrerequisiteStep> GetStepsFor(string gameName)
        {
            if (string.IsNullOrEmpty(gameName) || Prerequisites == null) return new List<LumaPrerequisiteStep>();
            return Prerequisites.TryGetValue(gameName, out var steps) && steps != null
                ? steps
                : new List<LumaPrerequisiteStep>();
        }

        /* Json.NET replaces Prerequisites with a fresh, default-comparer dictionary during
           deserialization regardless of the property initializer above — same gap
           RemoteManifest.Normalize exists to close. */
        public static void Normalize(LumaPrerequisitesManifest manifest)
        {
            if (manifest?.Prerequisites == null) return;

            var result = new Dictionary<string, List<LumaPrerequisiteStep>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in manifest.Prerequisites)
            {
                result[kv.Key] = kv.Value;
            }
            manifest.Prerequisites = result;
        }
    }
}
