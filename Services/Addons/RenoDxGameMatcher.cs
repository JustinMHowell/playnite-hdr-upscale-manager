using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HdrUpscaleManager.Models.Addons;

namespace HdrUpscaleManager.Services.Addons
{
    /* Matches a Playnite game name against RenoDX's wiki mod list, ported from RHI's
       GameDetectionService.MatchGame/NormalizeName. Names are normalized (diacritics,
       punctuation and case stripped) before comparing, since wiki titles and store titles
       rarely agree on capitalization or trademark symbols. */
    public static class RenoDxGameMatcher
    {
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            name = name.Replace("™", "").Replace("®", "").Replace("©", "");

            var decomposed = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return Regex.Replace(sb.ToString().ToLowerInvariant(), "[^a-z0-9]", "");
        }

        public static RenoDxWikiMod Match(
            string gameName,
            IEnumerable<RenoDxWikiMod> mods,
            IDictionary<string, string> nameOverrides = null)
        {
            var modList = mods.Select(m => new { Mod = m, Norm = NormalizeName(m.Name) }).ToList();
            var name = NormalizeName(gameName);

            /* A manifest-supplied override (detected name -> wiki name) always wins. */
            if (nameOverrides != null && nameOverrides.TryGetValue(gameName, out var mapped) &&
                !string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = NormalizeName(mapped);
                var byOverride = modList.FirstOrDefault(t => t.Norm == mappedNorm);
                if (byOverride != null) return byOverride.Mod;
            }

            var exact = modList.FirstOrDefault(t => t.Norm == name);
            if (exact != null) return exact.Mod;

            /* Detected name contains the mod name, e.g. "Code Vein GOTY Edition" vs wiki
               "Code Vein" — take the longest (most specific) mod name that still fits. */
            var containedBy = modList
                .Where(t => name.Contains(t.Norm))
                .OrderByDescending(t => t.Norm.Length)
                .FirstOrDefault();
            if (containedBy != null) return containedBy.Mod;

            /* Mod name contains the detected name (an abbreviated title) — only when the
               detected name is a prefix, and the remainder isn't a sequel marker (that would
               make it a different game, e.g. "Doom" must not match wiki "Doom 3"). */
            foreach (var t in modList.Where(t => t.Norm.StartsWith(name)).OrderBy(t => t.Norm.Length))
            {
                var suffix = t.Norm.Substring(name.Length);
                if (string.IsNullOrEmpty(suffix)) return t.Mod;
                if (!Regex.IsMatch(suffix, "^[ivxlcdm0-9]+$")) return t.Mod;
            }

            return null;
        }
    }
}
