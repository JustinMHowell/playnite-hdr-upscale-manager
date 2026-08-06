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
        private JsonRecordStore<DxvkInstalledRecord> DxvkRecords =>
            new JsonRecordStore<DxvkInstalledRecord>(GetPluginUserDataPath(), "dxvk_installs.json");

        private IEnumerable<GameMenuItem> BuildDxvkMenuItems(Playnite.SDK.Models.Game game)
        {
            var cachedManifest = new ManifestService(GetPluginUserDataPath()).TryGetCachedManifest();
            if (cachedManifest?.IsDxvkBlacklisted(game.Name) == true) yield break;

            yield return new GameMenuItem
            {
                Description = "Install DXVK+Lilium HDR (DX9/DX10)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallDxvkAsync(game)
            };

            var record = DxvkRecords.Get(game.Id.ToString());
            if (record != null)
            {
                yield return new GameMenuItem
                {
                    Description = "Check DXVK+Lilium for update",
                    MenuSection = "HDR & Upscale Manager",
                    Action = async _ => await CheckDxvkUpdateAsync(record)
                };

                yield return new GameMenuItem
                {
                    Description = "Uninstall DXVK+Lilium",
                    MenuSection = "HDR & Upscale Manager",
                    Action = _ => UninstallDxvk(game, record)
                };
            }
        }

        private async Task InstallDxvkAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                var cacheDir = Path.Combine(GetPluginUserDataPath(), "DxvkCache");
                var result = await DxvkService.InstallAsync(game.InstallDirectory, cacheDir).ConfigureAwait(false);

                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install DXVK+Lilium HDR");
                    return;
                }

                DxvkRecords.Set(game.Id.ToString(), new DxvkInstalledRecord
                {
                    GameId = game.Id.ToString(),
                    GameName = game.Name,
                    InstallPath = game.InstallDirectory,
                    InstalledDlls = result.InstalledDlls,
                    BackedUpFiles = result.BackedUpFiles,
                    DeployedConf = result.DeployedConf,
                    Version = result.Version,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                PlayniteApi.Dialogs.ShowMessage(
                    $"Installed DXVK+Lilium HDR {result.Version} ({string.Join(", ", result.InstalledDlls)}) to:\n{game.InstallDirectory}\n\n{result.Warning}",
                    "Install DXVK+Lilium HDR");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"DXVK+Lilium install failed: {ex.Message}", "Install DXVK+Lilium HDR");
            }
        }

        private async Task CheckDxvkUpdateAsync(DxvkInstalledRecord record)
        {
            try
            {
                var check = await DxvkService.CheckForUpdateAsync(record).ConfigureAwait(false);
                var message = check.Status switch
                {
                    DxvkUpdateStatus.UpToDate => $"'{record.GameName}' already has the current Lilium HDR DXVK build ({record.Version}) installed.",
                    DxvkUpdateStatus.UpdateAvailable => $"A newer Lilium HDR DXVK build is available for '{record.GameName}' ({record.Version} -> {check.LatestVersion}). Reinstall via \"Install DXVK+Lilium HDR\" to update.",
                    DxvkUpdateStatus.CouldNotCheck => "Could not reach the Lilium HDR DXVK GitHub release feed to check for updates.",
                    _ => "Unknown update status."
                };

                PlayniteApi.Dialogs.ShowMessage(message, "DXVK+Lilium Update Check");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium update check failed for '{record.GameName}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"DXVK+Lilium update check failed: {ex.Message}", "DXVK+Lilium Update Check");
            }
        }

        private void UninstallDxvk(Playnite.SDK.Models.Game game, DxvkInstalledRecord record)
        {
            try
            {
                var result = DxvkService.Uninstall(record);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Uninstall DXVK+Lilium");
                    return;
                }

                DxvkRecords.Remove(record.GameId);
                PlayniteApi.Dialogs.ShowMessage($"Uninstalled DXVK+Lilium from '{game.Name}'.", "Uninstall DXVK+Lilium");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium uninstall failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"DXVK+Lilium uninstall failed: {ex.Message}", "Uninstall DXVK+Lilium");
            }
        }
    }
}
