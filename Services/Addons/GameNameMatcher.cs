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
        /* Suffixes that mean "same game, different release" rather than "different game" —
           found by cross-checking real wiki data: without this whitelist, a bare "not purely
           numeric" check let "Doom" match wiki "Doom Eternal", "Hollow Knight" match wiki
           "Hollow Knight: Silksong", "Dishonored" match "Dishonored 2 + Death of the Outsider",
           etc. Anything not on this list is treated the same as a numbered sequel: no match. */
        private static readonly HashSet<string> EditionSuffixes = new HashSet<string>
        {
            "definitiveedition", "remastered", "enhancededition", "goldedition",
            "completeedition", "specialedition", "ultimateedition", "deluxeedition",
            "anniversaryedition", "directorscut", "gameoftheyearedition", "gotyedition",
            "legendaryedition", "standardedition"
        };

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

        /* Same normalization as NormalizeName, but kept as whole words instead of one concatenated
           blob. The "contains"/"starts with" fallbacks below need word boundaries — otherwise a
           short title like "Fe" matches as a raw substring of "Mass Effect" ("...ef..."), "Stray"
           matches inside "MO:Astray", "Raft" matches inside "Lovecraft's Untold Stories", etc. */
        private static List<string> Tokenize(string name)
        {
            if (string.IsNullOrEmpty(name)) return new List<string>();

            name = name.Replace("™", "").Replace("®", "").Replace("©", "");

            var decomposed = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            var tokens = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(sb.ToString().ToLowerInvariant(), "[a-z0-9]+"))
                tokens.Add(m.Value);
            return tokens;
        }

        private static bool HasSubsequenceAt(List<string> haystack, List<string> needle, int startIndex)
        {
            for (var i = 0; i < needle.Count; i++)
            {
                if (haystack[startIndex + i] != needle[i]) return false;
            }
            return true;
        }

        private static bool ContainsWholeWords(List<string> haystack, List<string> needle)
        {
            if (needle.Count == 0 || needle.Count > haystack.Count) return false;
            for (var i = 0; i <= haystack.Count - needle.Count; i++)
            {
                if (HasSubsequenceAt(haystack, needle, i)) return true;
            }
            return false;
        }

        public static T Match<T>(
            string gameName,
            IEnumerable<T> candidates,
            System.Func<T, string> nameSelector,
            IDictionary<string, string> nameOverrides = null) where T : class
        {
            var candidateList = candidates
                .Select(c => new { Item = c, Norm = NormalizeName(nameSelector(c)), Tokens = Tokenize(nameSelector(c)) })
                .ToList();
            var name = NormalizeName(gameName);
            var nameTokens = Tokenize(gameName);

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
               "Code Vein" — take the longest (most specific) name that still fits. Matched on
               whole word boundaries, not raw substring (see Tokenize's comment). */
            var containedBy = candidateList
                .Where(t => t.Tokens.Count > 0 && ContainsWholeWords(nameTokens, t.Tokens))
                .OrderByDescending(t => t.Tokens.Count)
                .FirstOrDefault();
            if (containedBy != null) return containedBy.Item;

            /* Candidate name contains the detected name (an abbreviated title) — only when the
               detected name is a whole-word prefix, and the remainder is a known "same game"
               release suffix rather than a different title entirely (e.g. "Doom" must not match
               wiki "Doom 3", nor "Doom Eternal"; "Ori and the Blind Forest" must match
               "...: Definitive Edition"). */
            foreach (var t in candidateList
                .Where(t => t.Tokens.Count >= nameTokens.Count && HasSubsequenceAt(t.Tokens, nameTokens, 0))
                .OrderBy(t => t.Tokens.Count))
            {
                var suffixTokens = t.Tokens.Skip(nameTokens.Count).ToList();
                if (suffixTokens.Count == 0) return t.Item;
                if (EditionSuffixes.Contains(string.Concat(suffixTokens))) return t.Item;
            }

            return null;
        }

        /* Returns the given name with a trailing "same game, different release" suffix removed —
           e.g. "Control Ultimate Edition" -> "Control" — reusing the EditionSuffixes whitelist
           above. For a caller doing an external title search rather than matching against a
           known candidate list (PcGamingWikiService's opensearch call, which has no such list),
           the plain edition-suffixed name often finds nothing at all: MediaWiki's opensearch does
           a prefix match, and "Control Ultimate Edition" isn't a prefix of the wiki's actual page
           title ("Control") — confirmed live 2026-08-08, zero results. Returns the original name
           unchanged if no known suffix is found at the end. */
        public static string StripKnownEditionSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            /* Up to 5 trailing tokens — "game of the year edition" is the longest phrase in
               EditionSuffixes (game/of/the/year/edition). */
            var tokenMatches = Regex.Matches(name, "[A-Za-z0-9]+");
            for (var take = 1; take <= 5 && take < tokenMatches.Count; take++)
            {
                var suffixBlob = string.Concat(tokenMatches
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Skip(tokenMatches.Count - take)
                    .Select(m => m.Value.ToLowerInvariant()));
                if (!EditionSuffixes.Contains(suffixBlob)) continue;

                var cutIndex = tokenMatches[tokenMatches.Count - take].Index;
                return name.Substring(0, cutIndex).TrimEnd(' ', ':', '-', '–', '—');
            }

            return name;
        }
    }
}
