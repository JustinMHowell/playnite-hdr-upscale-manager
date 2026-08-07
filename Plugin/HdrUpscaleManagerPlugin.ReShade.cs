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

        /* RenoDX and Luma are themselves ReShade addons — they need ReShade present as a host
           first, so a RenoDX/Luma install silently installs it first if it's missing rather
           than making the user click "Install ReShade" separately beforehand. Only surfaces a
           dialog if that step fails (blocking the RenoDX/Luma install that called it); success
           is just logged so a single install action doesn't pop two dialogs. Returns false if
           the caller should abort. */
        private async Task<bool> EnsureReShadeInstalledAsync(Playnite.SDK.Models.Game game, string forWhat)
        {
            if (ReShadeInstallService.IsInstalled(game.InstallDirectory)) return true;

            try
            {
                var cacheDir = Path.Combine(GetPluginUserDataPath(), "ReShadeCache");
                var result = await new ReShadeInstallService(cacheDir).InstallAsync(game.InstallDirectory);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"{forWhat} needs ReShade installed first, and that failed: {result.Error}",
                        $"Install {forWhat}");
                    return false;
                }

                Log.Info($"Auto-installed ReShade {result.Version} ({result.Api}) for '{game.Name}' ahead of {forWhat}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Auto ReShade install failed for '{game.Name}' ahead of {forWhat}.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    $"{forWhat} needs ReShade installed first, and that failed: {ex.Message}",
                    $"Install {forWhat}");
                return false;
            }
        }
    }
}
