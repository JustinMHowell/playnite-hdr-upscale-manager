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
        private JsonRecordStore<LumaInstalledRecord> LumaRecords =>
            new JsonRecordStore<LumaInstalledRecord>(GetPluginUserDataPath(), "luma_installs.json");

        private IEnumerable<GameMenuItem> BuildLumaMenuItems(Playnite.SDK.Models.Game game)
        {
            yield return new GameMenuItem
            {
                Description = "Install Luma (matched game)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallLumaMatchedAsync(game)
            };

            var record = LumaRecords.Get(game.Id.ToString());
            if (record != null)
            {
                yield return new GameMenuItem
                {
                    Description = "Check Luma for update",
                    MenuSection = "HDR & Upscale Manager",
                    Action = async _ => await CheckLumaUpdateAsync(record)
                };

                yield return new GameMenuItem
                {
                    Description = "Uninstall Luma",
                    MenuSection = "HDR & Upscale Manager",
                    Action = _ => UninstallLuma(game, record)
                };
            }
        }

        private async Task InstallLumaMatchedAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                var manifestResult = await new ManifestService(GetPluginUserDataPath()).FetchAsync().ConfigureAwait(false);
                var manifest = manifestResult?.Manifest;

                /* Luma and RenoDX are mutually exclusive per game by default (see
                   RemoteManifest.IsLumaRenodxCompatible) — block rather than silently
                   auto-uninstalling RenoDX on the user's behalf. */
                var renoDxRecord = RenoDxRecords.Get(game.Id.ToString());
                if (renoDxRecord != null && manifest?.IsLumaRenodxCompatible(game.Name) != true)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"RenoDX is currently installed for '{game.Name}'. Uninstall RenoDX first before installing Luma " +
                        "(this game isn't on the manifest's list of games known to run both at once).",
                        "Install Luma");
                    return;
                }

                if (!await EnsureReShadeInstalledAsync(game, "Luma")) return;

                var wikiCacheDir = Path.Combine(GetPluginUserDataPath(), "LumaWikiCache");
                var result = await LumaService
                    .InstallForGameAsync(game.InstallDirectory, game.Name, wikiCacheDir, manifest?.LumaNameOverrides)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install Luma");
                    return;
                }

                LumaRecords.Set(game.Id.ToString(), new LumaInstalledRecord
                {
                    GameId = game.Id.ToString(),
                    GameName = game.Name,
                    InstallPath = result.InstalledPath,
                    InstalledFiles = result.InstalledFiles,
                    DownloadUrl = result.DownloadUrl,
                    InstalledBuildNumber = await LumaService.GetLatestBuildNumberAsync().ConfigureAwait(false) ?? 0,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                var message = $"Installed Luma ({result.MatchedGameName}) to:\n{result.InstalledPath}";
                if (!string.IsNullOrEmpty(result.Notes))
                    message += $"\n\nNote: {result.Notes}";
                if (manifest?.LumaGameNotes != null && manifest.LumaGameNotes.TryGetValue(game.Name, out var noteEntry)
                    && !string.IsNullOrEmpty(noteEntry.Notes))
                    message += $"\n\n{noteEntry.Notes}";

                PlayniteApi.Dialogs.ShowMessage(message, "Install Luma");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Luma install failed: {ex.Message}", "Install Luma");
            }
        }

        private async Task CheckLumaUpdateAsync(LumaInstalledRecord record)
        {
            try
            {
                var check = await LumaService.CheckForUpdateAsync(record).ConfigureAwait(false);
                var message = check.Status switch
                {
                    LumaUpdateStatus.UpToDate => $"'{record.GameName}' already has the current Luma build ({record.InstalledBuildNumber}) installed.",
                    LumaUpdateStatus.UpdateAvailable => $"A newer Luma build is available for '{record.GameName}' ({record.InstalledBuildNumber} -> {check.LatestBuildNumber}). Reinstall via \"Install Luma (matched game)\" to update.",
                    LumaUpdateStatus.UnknownLocalInstall => $"'{record.GameName}' has a Luma install with no known build number — can't tell if it's current.",
                    LumaUpdateStatus.CouldNotCheck => "Could not reach the Luma Framework GitHub release feed to check for updates.",
                    _ => "Unknown update status."
                };

                PlayniteApi.Dialogs.ShowMessage(message, "Luma Update Check");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma update check failed for '{record.GameName}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Luma update check failed: {ex.Message}", "Luma Update Check");
            }
        }

        private void UninstallLuma(Playnite.SDK.Models.Game game, LumaInstalledRecord record)
        {
            try
            {
                var result = LumaService.Uninstall(record);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Uninstall Luma");
                    return;
                }

                LumaRecords.Remove(record.GameId);
                PlayniteApi.Dialogs.ShowMessage($"Uninstalled Luma from '{game.Name}'.", "Uninstall Luma");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma uninstall failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Luma uninstall failed: {ex.Message}", "Uninstall Luma");
            }
        }
    }
}
