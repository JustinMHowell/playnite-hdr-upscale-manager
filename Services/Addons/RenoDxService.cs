using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HdrUpscaleManager.Services.ReShade;

namespace HdrUpscaleManager.Services.Addons
{
    public class RenoDxAddonInfo
    {
        public string Url { get; set; }
        public string FileName { get; set; }
    }

    public class RenoDxMatchedInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledPath { get; set; }
        public string MatchedGameName { get; set; }
        public string Notes { get; set; }
    }

    /* Generic (engine-wide, not per-game) RenoDX addon URLs, plus the wiki-matched per-game
       install path — both observed in RHI's WikiService. Generic addons remain the fallback
       for games the wiki doesn't have a dedicated build for. */
    public static class RenoDxService
    {
        private const string UnrealAddonUrl =
            "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-unrealengine.addon64";
        private const string UnityAddonUrl64 =
            "https://github.com/NotVoosh/renodx-unity/releases/download/snapshot/renodx-unityengine.addon64";
        private const string UnityAddonUrl32 =
            "https://github.com/NotVoosh/renodx-unity/releases/download/snapshot/renodx-unityengine.addon32";

        public static RenoDxAddonInfo ResolveGenericAddon(RenoDxEngine engine, bool is64Bit)
        {
            if (engine == RenoDxEngine.UnrealEngine)
                return new RenoDxAddonInfo { Url = UnrealAddonUrl, FileName = "renodx-unrealengine.addon64" };

            return is64Bit
                ? new RenoDxAddonInfo { Url = UnityAddonUrl64, FileName = "renodx-unityengine.addon64" }
                : new RenoDxAddonInfo { Url = UnityAddonUrl32, FileName = "renodx-unityengine.addon32" };
        }

        public static async Task<AddonInstallResult> InstallGenericAsync(string gameInstallPath, RenoDxEngine engine)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            var is64Bit = exePath == null || PeInspector.Inspect(exePath)?.Is64Bit != false;
            var addon = ResolveGenericAddon(engine, is64Bit);

            var installer = new AddonInstallService();
            return await installer.InstallAsync(gameInstallPath, addon.Url, addon.FileName).ConfigureAwait(false);
        }

        /* Matches the game against RenoDX's wiki mod list before falling back to a generic
           engine-wide addon would be needed — this is the same per-game coverage RHI gets from
           its wiki scrape, so games with a dedicated build get that build instead of the
           generic one. */
        public static async Task<RenoDxMatchedInstallResult> InstallForGameAsync(
            string gameInstallPath,
            string gameName,
            string wikiCacheDirectory,
            IDictionary<string, string> wikiNameOverrides = null)
        {
            var wikiService = new RenoDxWikiService(wikiCacheDirectory);
            var mods = await wikiService.FetchAllAsync().ConfigureAwait(false);
            var match = RenoDxGameMatcher.Match(gameName, mods, wikiNameOverrides);
            if (match == null || (match.SnapshotUrl == null && match.SnapshotUrl32 == null))
            {
                return new RenoDxMatchedInstallResult
                {
                    Success = false,
                    Error = $"No RenoDX mod found for '{gameName}' on the wiki."
                };
            }

            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            var is64Bit = exePath == null || PeInspector.Inspect(exePath)?.Is64Bit != false;
            var url = is64Bit ? (match.SnapshotUrl ?? match.SnapshotUrl32) : (match.SnapshotUrl32 ?? match.SnapshotUrl);
            var fileName = Path.GetFileName(url);

            var installer = new AddonInstallService();
            var installResult = await installer.InstallAsync(gameInstallPath, url, fileName).ConfigureAwait(false);

            return new RenoDxMatchedInstallResult
            {
                Success = installResult.Success,
                Error = installResult.Error,
                InstalledPath = installResult.InstalledPath,
                MatchedGameName = match.Name,
                Notes = match.Notes
            };
        }
    }
}
