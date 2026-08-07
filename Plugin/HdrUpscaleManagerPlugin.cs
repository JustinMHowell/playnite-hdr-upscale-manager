using HdrUpscaleManager.Services;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HdrUpscaleManager.Plugin
{
    /* Split across HdrUpscaleManagerPlugin.*.cs partials, one per addon (ReShade/RenoDx/Luma/
       Dxvk/OptiScaler/Hdr), since a single file covering all of them would be unwieldy — mirrors
       RHI's own per-concern partial-class convention (MainViewModel.Install.cs, .BuildCards.cs, etc). */
    public partial class HdrUpscaleManagerPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        /* Playnite identifies add-ons by this Id — must be unique per plugin. */
        public override Guid Id { get; } = Guid.Parse("5C15D52C-22E9-4668-881D-C5DAF69A2F0E");

        public static string PluginVersion =>
            typeof(HdrUpscaleManagerPlugin).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HdrUpscaleManagerPlugin).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        public HdrUpscaleManagerPlugin(IPlayniteAPI api) : base(api)
        {
            Log.Initialize(System.IO.Path.Combine(GetPluginUserDataPath(), "logs"));
            logger.Info($"HDR & Upscale Manager v{PluginVersion} loaded.");
            Log.Info($"HDR & Upscale Manager v{PluginVersion} loaded.");
        }

        /* Fires once Playnite (Desktop or Fullscreen — this hook is mode-agnostic) has
           finished starting up. Refreshes the manifest cache in the background so it's
           current by the time the user opens a game menu, without blocking startup or
           showing any UI — read-only data hygiene, not an action taken on the user's
           behalf (PLAN.md §5). Failures are logged only; a stale/local cache is fine. */
        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                var result = await new ManifestService(GetPluginUserDataPath()).FetchAsync();
                Log.Info(result?.Manifest != null
                    ? $"Startup manifest refresh: source={result.Source}, version={result.Manifest.Version}."
                    : "Startup manifest refresh: no network and no local cache yet.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Startup manifest refresh failed.");
            }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Check manifest sync",
                MenuSection = "@HDR & Upscale Manager",
                Action = async _ => await CheckManifestSyncAsync()
            };
        }

        /* One entry, straight into the per-game window — every install/toggle/uninstall action
           for every component (ReShade/RenoDX/Luma/DXVK/OptiScaler/HDR toggle) lives there
           (ManagerViewModel), so there's nothing left for a right-click submenu to duplicate. */
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args.Games?.Where(g => !string.IsNullOrEmpty(g.InstallDirectory)).ToList()
                ?? new List<Playnite.SDK.Models.Game>();
            if (games.Count == 0) yield break;

            var game = games[0];
            var cachedManifest = new ManifestService(GetPluginUserDataPath()).TryGetCachedManifest();
            if (cachedManifest?.IsBlacklisted(game.Name) == true)
            {
                yield return new GameMenuItem
                {
                    Description = "Blacklisted by manifest — install unavailable",
                    MenuSection = "HDR & Upscale Manager",
                    Action = _ => PlayniteApi.Dialogs.ShowMessage(
                        $"'{game.Name}' is on the manifest's blacklist (usually anti-cheat games where modding risks a ban) — install actions are disabled for it.",
                        "HDR & Upscale Manager")
                };
                yield break;
            }

            yield return new GameMenuItem
            {
                Description = "Open in HDR & Upscale Manager…",
                MenuSection = "HDR & Upscale Manager",
                Action = _ => OpenManagerWindow(game)
            };
        }

        private async System.Threading.Tasks.Task CheckManifestSyncAsync()
        {
            try
            {
                var service = new ManifestService(GetPluginUserDataPath());
                var result = await service.FetchAsync();
                if (result?.Manifest == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "Could not fetch or load the manifest (no network and no local cache yet).",
                        "HDR & Upscale Manager");
                    return;
                }

                var m = result.Manifest;
                var shaLine = result.Sha != null ? $"Manifest blob sha: {result.Sha.Substring(0, 7)}\n" : "";
                var message =
                    $"Source: {result.Source}\n" +
                    shaLine +
                    $"Fetched: {result.FetchedAtUtc:u}\n\n" +
                    $"Manifest version: {m.Version}\n" +
                    $"Blacklisted apps: {m.Blacklist?.Count ?? 0}\n" +
                    $"Native HDR games: {m.NativeHdrGames?.Count ?? 0}\n" +
                    $"Luma default games: {m.LumaDefaultGames?.Count ?? 0}\n" +
                    $"Addon packs: {m.AddonPacks?.Count ?? 0}";

                PlayniteApi.Dialogs.ShowMessage(message, "HDR & Upscale Manager — Manifest Sync");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Manifest fetch failed.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Manifest fetch failed: {ex.Message}", "HDR & Upscale Manager");
            }
        }
    }
}
