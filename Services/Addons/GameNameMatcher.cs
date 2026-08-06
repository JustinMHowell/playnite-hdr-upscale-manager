using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HdrUpscaleManager.Services.Addons
{
    /* Shared game-name matching, ported from RHI's GameDetectionService.MatchGame/NormalizeName —
       used by every wiki-scraped addon source (RenoDX, Luma) since they all face the same problem:
       wiki titles and Playnite/store titles rarely agree on capitalization, trademark symbols,
       diacritics or punctuation. */
    public static class GameNameMatcher
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

        public static T Match<T>(
            string gameName,
            IEnumerable<T> candidates,
            System.Func<T, string> nameSelector,
            IDictionary<string, string> nameOverrides = null) where T : class
        {
            var candidateList = candidates.Select(c => new { Item = c, Norm = NormalizeName(nameSelector(c)) }).ToList();
            var name = NormalizeName(gameName);

            /* A manifest-supplied override (detected name -> wiki name) always wins. */
            if (nameOverrides != null && nameOverrides.TryGetValue(gameName, out var mapped) &&
                !string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = NormalizeName(mapped);
                var byOverride = candidateList.FirstOrDefault(t => t.Norm == mappedNorm);
                if (byOverride != null) return byOverride.Item;
            }

            var exact = candidateList.FirstOrDefault(t => t.Norm == name);
            if (exact != null) return exact.Item;

            /* Detected name contains the candidate name, e.g. "Code Vein GOTY Edition" vs wiki
               "Code Vein" — take the longest (most specific) name that still fits. */
            var containedBy = candidateList
                .Where(t => name.Contains(t.Norm))
                .OrderByDescending(t => t.Norm.Length)
                .FirstOrDefault();
            if (containedBy != null) return containedBy.Item;

            /* Candidate name contains the detected name (an abbreviated title) — only when the
               detected name is a prefix, and the remainder isn't a sequel marker (that would
               make it a different game, e.g. "Doom" must not match wiki "Doom 3"). */
            foreach (var t in candidateList.Where(t => t.Norm.StartsWith(name)).OrderBy(t => t.Norm.Length))
            {
                var suffix = t.Norm.Substring(name.Length);
                if (string.IsNullOrEmpty(suffix)) return t.Item;
                if (!Regex.IsMatch(suffix, "^[ivxlcdm0-9]+$")) return t.Item;
            }

            return null;
        }
    }
}
