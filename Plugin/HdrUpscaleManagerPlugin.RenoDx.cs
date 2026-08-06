using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        private JsonRecordStore<RenoDxInstalledRecord> RenoDxRecords =>
            new JsonRecordStore<RenoDxInstalledRecord>(GetPluginUserDataPath(), "renodx_installs.json");

        private IEnumerable<GameMenuItem> BuildRenoDxMenuItems(Playnite.SDK.Models.Game game)
        {
            yield return new GameMenuItem
            {
                Description = "Install RenoDX (matched game)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallRenoDxMatchedAsync(game)
            };

            yield return new GameMenuItem
            {
                Description = "Install RenoDX (auto-detect engine)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallRenoDxAutoDetectAsync(game)
            };

            yield return new GameMenuItem
            {
                Description = "Install RenoDX (Unreal Engine, generic)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallRenoDxAsync(game, RenoDxEngine.UnrealEngine, forceUeExtended: null)
            };

            yield return new GameMenuItem
            {
                Description = "Install RenoDX (Unreal Engine, UE-Extended)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallRenoDxAsync(game, RenoDxEngine.UnrealEngine, forceUeExtended: true)
            };

            yield return new GameMenuItem
            {
                Description = "Install RenoDX (Unity, generic)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallRenoDxAsync(game, RenoDxEngine.UnityEngine, forceUeExtended: false)
            };

            var record = RenoDxRecords.Get(game.Id.ToString());
            if (record != null)
            {
                yield return new GameMenuItem
                {
                    Description = "Check RenoDX for update",
                    MenuSection = "HDR & Upscale Manager",
                    Action = async _ => await CheckRenoDxUpdateAsync(game, record)
                };

                yield return new GameMenuItem
                {
                    Description = "Uninstall RenoDX",
                    MenuSection = "HDR & Upscale Manager",
                    Action = _ => UninstallRenoDx(game, record)
                };
            }
        }

        private async Task InstallRenoDxAsync(Playnite.SDK.Models.Game game, RenoDxEngine engine, bool? forceUeExtended)
        {
            try
            {
                RemoteManifest manifest = null;
                if (engine == RenoDxEngine.UnrealEngine)
                    manifest = (await new ManifestService(GetPluginUserDataPath()).FetchAsync().ConfigureAwait(false))?.Manifest;

                var useUeExtended = forceUeExtended ?? RenoDxService.ShouldUseUeExtended(game.Name, manifest);
                var result = await RenoDxService.InstallGenericAsync(game.InstallDirectory, engine, useUeExtended, manifest);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install RenoDX");
                    return;
                }

                PlayniteApi.Dialogs.ShowMessage($"Installed RenoDX to:\n{result.InstalledPath}", "Install RenoDX");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"RenoDX install failed: {ex.Message}", "Install RenoDX");
            }
        }

        private async Task InstallRenoDxAutoDetectAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                var manifest = (await new ManifestService(GetPluginUserDataPath()).FetchAsync().ConfigureAwait(false))?.Manifest;
                var detection = RenoDxEngineDetector.Detect(
                    game.InstallDirectory, game.Name, manifest?.InstallPathOverrides, manifest?.EngineHintOverrides);

                if (detection.Engine == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "Could not auto-detect the game's engine (Unreal Engine or Unity) — use one of the engine-specific menu items instead.",
                        "Install RenoDX");
                    return;
                }

                var useUeExtended = detection.Engine == RenoDxEngine.UnrealEngine &&
                    RenoDxService.ShouldUseUeExtended(game.Name, manifest);
                var result = await RenoDxService
                    .InstallGenericAsync(detection.InstallPath, detection.Engine.Value, useUeExtended, manifest)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install RenoDX");
                    return;
                }

                PlayniteApi.Dialogs.ShowMessage(
                    $"Detected {detection.Engine} — installed RenoDX to:\n{result.InstalledPath}", "Install RenoDX");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX auto-detect install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"RenoDX install failed: {ex.Message}", "Install RenoDX");
            }
        }

        private async Task InstallRenoDxMatchedAsync(Playnite.SDK.Models.Game game)
        {
            try
            {
                var manifestService = new ManifestService(GetPluginUserDataPath());
                var manifestResult = await manifestService.FetchAsync().ConfigureAwait(false);
                var nameOverrides = manifestResult?.Manifest?.WikiNameOverrides;

                var wikiCacheDir = Path.Combine(GetPluginUserDataPath(), "RenoDxWikiCache");
                var result = await RenoDxService
                    .InstallForGameAsync(game.InstallDirectory, game.Name, wikiCacheDir, nameOverrides)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    if (result.ExternalUrl != null)
                    {
                        var openIt = PlayniteApi.Dialogs.ShowMessage(
                            $"{result.Error}\n\n{result.ExternalUrl}\n\nOpen this link in your browser?",
                            "Install RenoDX",
                            MessageBoxButton.YesNo);
                        if (openIt == MessageBoxResult.Yes)
                            Process.Start(new ProcessStartInfo(result.ExternalUrl) { UseShellExecute = true });
                        return;
                    }

                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Install RenoDX");
                    return;
                }

                RenoDxRecords.Set(game.Id.ToString(), new RenoDxInstalledRecord
                {
                    GameId = game.Id.ToString(),
                    GameName = game.Name,
                    InstallPath = result.InstalledPath,
                    AddonFileName = result.AddonFileName,
                    MatchedWikiName = result.MatchedGameName,
                    DownloadUrl = result.DownloadUrl,
                    FileHash = result.FileHash,
                    RemoteContentLength = result.RemoteContentLength,
                    RemoteLastModified = result.RemoteLastModified,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                var message = $"Installed RenoDX ({result.MatchedGameName}) to:\n{result.InstalledPath}";
                if (!string.IsNullOrEmpty(result.Notes))
                    message += $"\n\nNote: {result.Notes}";

                PlayniteApi.Dialogs.ShowMessage(message, "Install RenoDX");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX matched install failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"RenoDX install failed: {ex.Message}", "Install RenoDX");
            }
        }

        private async Task CheckRenoDxUpdateAsync(Playnite.SDK.Models.Game game, RenoDxInstalledRecord record)
        {
            try
            {
                var manifestResult = await new ManifestService(GetPluginUserDataPath()).FetchAsync().ConfigureAwait(false);
                var wikiCacheDir = Path.Combine(GetPluginUserDataPath(), "RenoDxWikiCache");
                var check = await RenoDxService
                    .CheckForUpdateAsync(record, wikiCacheDir, manifestResult?.Manifest?.WikiNameOverrides)
                    .ConfigureAwait(false);

                var message = check.Status switch
                {
                    RenoDxUpdateStatus.UpToDate => $"'{record.GameName}' already has the current RenoDX snapshot installed.",
                    RenoDxUpdateStatus.UpdateAvailable => $"A newer RenoDX snapshot is available for '{check.MatchedGameName ?? record.GameName}'. Reinstall via \"Install RenoDX (matched game)\" to update.",
                    RenoDxUpdateStatus.NoLongerListed => $"'{record.GameName}' no longer has a matching entry on the RenoDX wiki — the installed addon may be stale.",
                    _ => "Unknown update status."
                };

                PlayniteApi.Dialogs.ShowMessage(message, "RenoDX Update Check");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX update check failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"RenoDX update check failed: {ex.Message}", "RenoDX Update Check");
            }
        }

        private void UninstallRenoDx(Playnite.SDK.Models.Game game, RenoDxInstalledRecord record)
        {
            try
            {
                var result = RenoDxService.Uninstall(record);
                if (!result.Success)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(result.Error, "Uninstall RenoDX");
                    return;
                }

                RenoDxRecords.Remove(record.GameId);
                PlayniteApi.Dialogs.ShowMessage($"Uninstalled RenoDX from '{game.Name}'.", "Uninstall RenoDX");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX uninstall failed for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"RenoDX uninstall failed: {ex.Message}", "Uninstall RenoDX");
            }
        }
    }
}
