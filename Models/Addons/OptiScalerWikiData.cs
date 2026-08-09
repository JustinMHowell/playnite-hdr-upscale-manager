using System;
using System.Collections.Generic;

namespace HdrUpscaleManager.Models.Addons
{
    public class OptiScalerCompatEntry
    {
        public string GameName { get; set; } = "";
        public string Status { get; set; } = "";
        public List<string> Upscalers { get; set; } = new List<string>();
        public string Notes { get; set; }
        public string DetailPageUrl { get; set; }
    }

    /* Parsed from OptiScaler's own GitHub wiki (Compatibility List + FSR4 Compatibility List) —
       same source and table shape RHI's own OptiScalerWikiService reads, ported with credit (see
       THIRD_PARTY_NOTICES.md). */
    public class OptiScalerWikiData
    {
        public Dictionary<string, OptiScalerCompatEntry> StandardCompat { get; set; } =
            new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OptiScalerCompatEntry> Fsr4Compat { get; set; } =
            new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);

        /* Gates OptiScalerWikiService.FetchAsync's own re-fetch — a wiki that only changes as
           games get patched doesn't need hitting GitHub every time a manager window opens. */
        public DateTime FetchedAtUtc { get; set; }

        /* Json.NET replaces these dictionaries with fresh, default-comparer instances during
           deserialization regardless of the property initializer above — same gap
           RemoteManifest.Normalize exists to close. Only needed after reading the on-disk cache
           back; the parser builds these directly with the right comparer already. */
        public static void Normalize(OptiScalerWikiData data)
        {
            if (data == null) return;
            data.StandardCompat = CaseInsensitive(data.StandardCompat);
            data.Fsr4Compat = CaseInsensitive(data.Fsr4Compat);
        }

        private static Dictionary<string, OptiScalerCompatEntry> CaseInsensitive(Dictionary<string, OptiScalerCompatEntry> source)
        {
            if (source == null) return new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in source)
            {
                result[kv.Key] = kv.Value;
            }
            return result;
        }
    }
}
