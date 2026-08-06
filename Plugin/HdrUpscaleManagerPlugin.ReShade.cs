using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.ReShade;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        private IEnumerable<GameMenuItem> BuildReShadeMenuItems(Playnite.SDK.Models.Game game)
        {
            yield return new GameMenuItem
            {
                Description = "Install ReShade (latest)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallReShadeAsync(game)
            };
        }

        private async Task InstallReShadeAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                var cacheDir = Path.Combine(GetPluginUserDataPath(), "ReShadeCache");
                var service = new ReShadeInstallService(cacheDir);
                var result = await service.InstallAsync(game.InstallDirectory);

                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install ReShade");
                    return;
                }

                PlayniteApi.Dialogs.ShowMessage(
                    $"Installed ReShade {result.Version} ({result.Api}) to:\n{result.InstalledDllPath}",
                    "Install ReShade");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"ReShade install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"ReShade install failed: {ex.Message}", "Install ReShade");
            }
        }
    }
}
