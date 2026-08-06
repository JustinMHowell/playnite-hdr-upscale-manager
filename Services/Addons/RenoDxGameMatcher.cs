using System.Collections.Generic;
using HdrUpscaleManager.Models.Addons;

namespace HdrUpscaleManager.Services.Addons
{
    /* Thin RenoDX-specific wrapper over the shared GameNameMatcher (ported from RHI's
       GameDetectionService.MatchGame/NormalizeName). */
    public static class RenoDxGameMatcher
    {
        public static string NormalizeName(string name) => GameNameMatcher.NormalizeName(name);

        public static RenoDxWikiMod Match(
            string gameName,
            IEnumerable<RenoDxWikiMod> mods,
            IDictionary<string, string> nameOverrides = null)
        {
            return GameNameMatcher.Match(gameName, mods, m => m.Name, nameOverrides);
        }
    }
}
