using System.Threading.Tasks;
using HdrUpscaleManager.Services.ReShade;

namespace HdrUpscaleManager.Services.Addons
{
    public class RenoDxAddonInfo
    {
        public string Url { get; set; }
        public string FileName { get; set; }
    }

    /* Generic (engine-wide, not per-game) RenoDX addon URLs observed in RHI's WikiService —
       the same fallback addons RHI installs for games without a dedicated build. Per-game
       specific matching (RHI's wiki-scraped mod list) isn't implemented here yet — that's a
       separate, much larger feature (HTML scraping, fuzzy name matching) for later. */
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
    }
}
