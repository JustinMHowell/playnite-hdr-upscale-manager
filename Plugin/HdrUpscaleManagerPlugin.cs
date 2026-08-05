using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.ReShade;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace HdrUpscaleManager.Plugin
{
    public class HdrUpscaleManagerPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        /* Playnite identifies add-ons by this Id — must be unique per plugin. */
        public override Guid Id { get; } = Guid.Parse("5C15D52C-22E9-4668-881D-C5DAF69A2F0E");

        private readonly HdrUpscaleManagerSettings settings;

        public static string PluginVersion =>
            typeof(HdrUpscaleManagerPlugin).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HdrUpscaleManagerPlugin).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        public HdrUpscaleManagerPlugin(IPlayniteAPI api) : base(api)
        {
            Log.Initialize(System.IO.Path.Combine(GetPluginUserDataPath(), "logs"));
            settings = new HdrUpscaleManagerSettings(this);
            Properties = new GenericPluginProperties { HasSettings = true };
            logger.Info($"HDR & Upscale Manager v{PluginVersion} loaded.");
            Log.Info($"HDR & Upscale Manager v{PluginVersion} loaded.");
        }

        public override ISettings GetSettings(bool firstRunSettings) => settings;

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new HdrUpscaleManagerSettingsView { DataContext = settings };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Say hello",
                MenuSection = "@HDR & Upscale Manager",
                Action = _ => PlayniteApi.Dialogs.ShowMessage(settings.Greeting, "HDR & Upscale Manager")
            };

            yield return new MainMenuItem
            {
                Description = "Check manifest sync",
                MenuSection = "@HDR & Upscale Manager",
                Action = async _ => await CheckManifestSyncAsync()
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args.Games?.Where(g => !string.IsNullOrEmpty(g.InstallDirectory)).ToList()
                ?? new List<Playnite.SDK.Models.Game>();
            if (games.Count == 0) yield break;

            yield return new GameMenuItem
            {
                Description = "Install ReShade (latest)",
                MenuSection = "HDR & Upscale Manager",
                Action = async _ => await InstallReShadeAsync(games[0])
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

        private async Task CheckManifestSyncAsync()
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
