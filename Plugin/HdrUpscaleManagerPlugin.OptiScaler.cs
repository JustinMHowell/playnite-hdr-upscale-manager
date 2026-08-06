using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        private JsonRecordStore<OptiScalerInstalledRecord> OptiScalerRecords =>
            new JsonRecordStore<OptiScalerInstalledRecord>(GetPluginUserDataPath(), "optiscaler_installs.json");

        private IEnumerable<GameMenuItem> BuildOptiScalerMenuItems(Playnite.SDK.Models.Game game)
        {
            yield return new GameMenuItem
            {
                Description = "Install OptiScaler (AMD, DLSS-only game)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallOptiScalerAsync(game, dlssInputs: true)
            };

            yield return new GameMenuItem
            {
                Description = "Install OptiScaler (AMD, has native FSR/XeSS)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallOptiScalerAsync(game, dlssInputs: false)
            };

            var record = OptiScalerRecords.Get(game.Id.ToString());
            if (record != null)
            {
                yield return new GameMenuItem
                {
                    Description = "Check OptiScaler for update",
                    MenuSection = "HDR & Upscale Manager",
                    Action = async _ => await CheckOptiScalerUpdateAsync(record)
                };

                yield return new GameMenuItem
                {
                    Description = "Uninstall OptiScaler",
                    MenuSection = "HDR & Upscale Manager",
                    Action = _ => UninstallOptiScaler(game, record)
                };
            }
        }

        private async Task InstallOptiScalerAsync(Playnite.SDK.Models.Game game, bool dlssInputs)
        {
            try
            {
                var manifestResult = await new ManifestService(GetPluginUserDataPath()).FetchAsync().ConfigureAwait(false);
                var cacheDir = Path.Combine(GetPluginUserDataPath(), "OptiScalerCache");

                var result = await OptiScalerService
                    .InstallAsync(game.InstallDirectory, game.Name, dlssInputs, cacheDir, manifestResult?.Manifest)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install OptiScaler");
                    return;
                }

                OptiScalerRecords.Set(game.Id.ToString(), new OptiScalerInstalledRecord
                {
                    GameId = game.Id.ToString(),
                    GameName = game.Name,
                    InstallPath = game.InstallDirectory,
                    DllName = result.DllName,
                    InstalledFiles = result.InstalledFiles,
                    DlssInputs = dlssInputs,
                    OptiPatcherInstalled = result.OptiPatcherInstalled,
                    DlssDllInstalled = result.DlssDllInstalled,
                    Version = result.Version,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                var message = $"Installed OptiScaler {result.Version} to:\n{game.InstallDirectory}\n\n" +
                    $"Proxy DLL: {result.DllName}\n" +
                    $"OptiPatcher: {(result.OptiPatcherInstalled ? "installed" : "not installed — AMD/Intel DX11-on-12 patch skipped")}\n" +
                    $"DLSS-input DLL: {(result.DlssDllInstalled ? "staged" : "not staged — DLSS-only substitution unavailable for this game until it is")}";

                PlayniteApi.Dialogs.ShowMessage(message, "Install OptiScaler");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"OptiScaler install failed: {ex.Message}", "Install OptiScaler");
            }
        }

        private async Task CheckOptiScalerUpdateAsync(OptiScalerInstalledRecord record)
        {
            try
            {
                var check = await OptiScalerService.CheckForUpdateAsync(record).ConfigureAwait(false);
                var message = check.Status switch
                {
                    OptiScalerUpdateStatus.UpToDate => $"'{record.GameName}' already has the current OptiScaler build ({record.Version}) installed.",
                    OptiScalerUpdateStatus.UpdateAvailable => $"A newer OptiScaler build is available for '{record.GameName}' ({record.Version} -> {check.LatestVersion}). Reinstall to update.",
                    OptiScalerUpdateStatus.CouldNotCheck => "Could not reach the OptiScaler GitHub release feed to check for updates.",
                    _ => "Unknown update status."
                };

                PlayniteApi.Dialogs.ShowMessage(message, "OptiScaler Update Check");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler update check failed for '{record.GameName}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"OptiScaler update check failed: {ex.Message}", "OptiScaler Update Check");
            }
        }

        private void UninstallOptiScaler(Playnite.SDK.Models.Game game, OptiScalerInstalledRecord record)
        {
            try
            {
                var result = OptiScalerService.Uninstall(record);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Uninstall OptiScaler");
                    return;
                }

                OptiScalerRecords.Remove(record.GameId);
                PlayniteApi.Dialogs.ShowMessage($"Uninstalled OptiScaler from '{game.Name}'.", "Uninstall OptiScaler");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler uninstall failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"OptiScaler uninstall failed: {ex.Message}", "Uninstall OptiScaler");
            }
        }
    }
}
