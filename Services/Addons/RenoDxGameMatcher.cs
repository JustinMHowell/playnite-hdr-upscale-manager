using System.Collections.Generic;
using System.Linq;
using HdrUpscaleManager.Models.Addons;

namespace HdrUpscaleManager.Services.Addons
{
    /* Thin RenoDX-specific wrapper over the shared GameNameMatcher (ported from RHI's
       GameDetectionService.MatchGame/NormalizeName). */
    public static class RenoDxGameMatcher
    {
        public static string NormalizeName(string name) => GameNameMatcher.NormalizeName(name);

        /* Only matches installable per-game mods — the wiki's engine compatibility lists
           (Unreal/Unity) name games too, but those rows never carry a download of their own.
           Letting them match made InstallRenoDxAsync stop at "listed on the RenoDX wiki but has
           no download link yet" instead of falling through to the generic engine install those
           lists exist to describe. */
        public static RenoDxWikiMod Match(
            string gameName,
            IEnumerable<RenoDxWikiMod> mods,
            IDictionary<string, string> nameOverrides = null)
        {
            return GameNameMatcher.Match(gameName, mods.Where(m => !m.IsEngineCompatEntry), m => m.Name, nameOverrides);
        }

        /* Every wiki note that applies to this game: its own mod row's note (status tooltip or
           notes column), plus one line per engine compatibility list it appears in, labelled by
           that list's section heading. Empty when the wiki says nothing about the game. */
        public static List<string> FindWikiNotes(
            string gameName,
            IEnumerable<RenoDxWikiMod> mods,
            IDictionary<string, string> nameOverrides = null)
        {
            var all = mods.ToList();
            var notes = new List<string>();

            var mod = Match(gameName, all, nameOverrides);
            if (mod != null)
            {
                if (mod.IsWorkInProgress)
                    notes.Add("Work in progress" + (HasText(mod.Notes) ? ": " + Tidy(mod.Notes) : "."));
                else if (HasText(mod.Notes))
                    notes.Add(Tidy(mod.Notes));
            }

            foreach (var list in all.Where(m => m.IsEngineCompatEntry).GroupBy(m => m.Section))
            {
                var entry = GameNameMatcher.Match(gameName, list, m => m.Name, nameOverrides);
                if (entry != null && HasText(entry.Notes))
                    notes.Add($"{list.Key} list: {Tidy(entry.Notes)}");
            }

            return notes;
        }

        private static bool HasText(string s) => !string.IsNullOrWhiteSpace(s);

        // BuildNoteText wraps <code> spans in backticks — plain text reads better in a TextBlock.
        private static string Tidy(string note) => note.Replace("`", "").Trim();
    }
}
