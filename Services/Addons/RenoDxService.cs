using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
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
        /* Set when the matched mod has no direct addon download, only a Nexus/Discord
           link — Success stays false since nothing was installed automatically. */
        public string ExternalUrl { get; set; }
        public string ExternalUrlLabel { get; set; }
        /* Populated on success, for building a RenoDxInstalledRecord. */
        public string DownloadUrl { get; set; }
        public string AddonFileName { get; set; }
        public string FileHash { get; set; }
        public long? RemoteContentLength { get; set; }
        public DateTimeOffset? RemoteLastModified { get; set; }
    }

    public enum RenoDxUpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        NoLongerListed
    }

    public class RenoDxUpdateCheckResult
    {
        public RenoDxUpdateStatus Status { get; set; }
        public string MatchedGameName { get; set; }
        public string CurrentUrl { get; set; }
    }

    public class RenoDxUninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class RenoDxSwapResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> SwappedFiles { get; set; } = new List<string>();
        /* When the build now live was installed (its file's last-write time). */
        public DateTime? NowActiveInstalledAt { get; set; }
    }

    public enum RenoDxInstallPlanKind
    {
        DedicatedMod,
        DedicatedModExternalOnly,
        DedicatedModNoDownload,
        GenericEngine,
        NoneAvailable
    }

    /* What the manager window's Install button would do for a game, worked out without
       installing anything — see RenoDxService.PlanInstall. */
    public class RenoDxInstallPlan
    {
        public RenoDxInstallPlanKind Kind { get; set; }
        public string MatchedGameName { get; set; }
        public string ExternalUrlLabel { get; set; }
        public RenoDxEngine? Engine { get; set; }
        public bool UseUeExtended { get; set; }

        public string Describe()
        {
            switch (Kind)
            {
                case RenoDxInstallPlanKind.DedicatedMod:
                    return $"Dedicated mod on the RenoDX wiki: \"{MatchedGameName}\" — Install uses its per-game build.";
                case RenoDxInstallPlanKind.DedicatedModExternalOnly:
                    return $"Dedicated mod on the RenoDX wiki: \"{MatchedGameName}\" — only distributed via {ExternalUrlLabel}, so Install can't download it automatically.";
                case RenoDxInstallPlanKind.DedicatedModNoDownload:
                    return $"Dedicated mod on the RenoDX wiki: \"{MatchedGameName}\" — listed, but it has no download link yet.";
                case RenoDxInstallPlanKind.GenericEngine:
                    var build = Engine == RenoDxEngine.UnrealEngine
                        ? (UseUeExtended ? "Unreal Engine (UE-Extended)" : "Unreal Engine")
                        : "Unity";
                    return $"No dedicated mod on the RenoDX wiki — Install falls back to the generic {build} build.";
                default:
                    return "No dedicated mod on the RenoDX wiki, and the engine couldn't be detected from the game's files — Install can't pick a generic build; choose one below.";
            }
        }
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

        /* Alternate Unreal Engine build that also deploys native-HDR Engine.ini settings,
           observed in RHI (RenoDXCommander.MainViewModel.Install.DefaultUeExtendedUrl) —
           overridable per-manifest via RemoteManifest.ComponentUrls["ueExtended"]. */
        public const string UeExtendedAddonFileName = "renodx-ue-extended.addon64";
        private const string DefaultUeExtendedUrl = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";

        public static RenoDxAddonInfo ResolveGenericAddon(
            RenoDxEngine engine, bool is64Bit, bool useUeExtended = false, RemoteManifest manifest = null)
        {
            if (engine == RenoDxEngine.UnrealEngine && useUeExtended)
            {
                var url = manifest?.ComponentUrls != null &&
                    manifest.ComponentUrls.TryGetValue("ueExtended", out var overrideUrl) &&
                    !string.IsNullOrEmpty(overrideUrl)
                    ? overrideUrl
                    : DefaultUeExtendedUrl;
                return new RenoDxAddonInfo { Url = url, FileName = UeExtendedAddonFileName };
            }

            if (engine == RenoDxEngine.UnrealEngine)
                return new RenoDxAddonInfo { Url = UnrealAddonUrl, FileName = "renodx-unrealengine.addon64" };

            return is64Bit
                ? new RenoDxAddonInfo { Url = UnityAddonUrl64, FileName = "renodx-unityengine.addon64" }
                : new RenoDxAddonInfo { Url = UnityAddonUrl32, FileName = "renodx-unityengine.addon32" };
        }

        /* Per-game UE-Extended default, ported from RHI's MainViewModel.BuildCards priority
           order (NativeHdrGames beats NoUeExtendedGames; RHI's own new default is UE-Extended
           for any generic Unreal Engine install once neither list says otherwise). */
        public static bool ShouldUseUeExtended(string gameName, RemoteManifest manifest)
        {
            if (manifest == null || string.IsNullOrEmpty(gameName)) return true;

            if (manifest.NativeHdrGames != null &&
                manifest.NativeHdrGames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
                return true;

            if (manifest.NoUeExtendedGames != null &&
                manifest.NoUeExtendedGames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public static async Task<AddonInstallResult> InstallGenericAsync(
            string gameInstallPath, RenoDxEngine engine, bool useUeExtended = false, RemoteManifest manifest = null)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            var is64Bit = exePath == null || PeInspector.Inspect(exePath)?.Is64Bit != false;
            var addon = ResolveGenericAddon(engine, is64Bit, useUeExtended, manifest);

            var installer = new AddonInstallService();
            return await installer.InstallAsync(gameInstallPath, addon.Url, addon.FileName).ConfigureAwait(false);
        }

        /* Dry run of the manager window's Install button: the same wiki match InstallForGameAsync
           makes, then the same generic fallback. locallyDetectedEngine must be what
           RenoDxEngineDetector found in the game's files — that fallback doesn't consult
           PCGamingWiki. */
        public static RenoDxInstallPlan PlanInstall(
            string gameName,
            IEnumerable<RenoDxWikiMod> mods,
            IDictionary<string, string> wikiNameOverrides,
            RenoDxEngine? locallyDetectedEngine,
            RemoteManifest manifest)
        {
            var match = RenoDxGameMatcher.Match(gameName, mods, wikiNameOverrides);
            if (match != null)
            {
                if (match.SnapshotUrl != null || match.SnapshotUrl32 != null)
                    return new RenoDxInstallPlan { Kind = RenoDxInstallPlanKind.DedicatedMod, MatchedGameName = match.Name };

                if (match.NexusUrl == null && match.DiscordUrl == null)
                    return new RenoDxInstallPlan { Kind = RenoDxInstallPlanKind.DedicatedModNoDownload, MatchedGameName = match.Name };

                return new RenoDxInstallPlan
                {
                    Kind = RenoDxInstallPlanKind.DedicatedModExternalOnly,
                    MatchedGameName = match.Name,
                    ExternalUrlLabel = match.NexusUrl != null ? "Nexus Mods" : "Discord"
                };
            }

            if (locallyDetectedEngine == null)
                return new RenoDxInstallPlan { Kind = RenoDxInstallPlanKind.NoneAvailable };

            return new RenoDxInstallPlan
            {
                Kind = RenoDxInstallPlanKind.GenericEngine,
                Engine = locallyDetectedEngine,
                UseUeExtended = locallyDetectedEngine == RenoDxEngine.UnrealEngine && ShouldUseUeExtended(gameName, manifest)
            };
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
            if (match == null)
            {
                return new RenoDxMatchedInstallResult
                {
                    Success = false,
                    Error = $"No RenoDX mod found for '{gameName}' on the wiki."
                };
            }

            if (match.SnapshotUrl == null && match.SnapshotUrl32 == null)
            {
                var externalUrl = match.NexusUrl ?? match.DiscordUrl;
                if (externalUrl == null)
                {
                    return new RenoDxMatchedInstallResult
                    {
                        Success = false,
                        MatchedGameName = match.Name,
                        Error = $"'{match.Name}' is listed on the RenoDX wiki but has no download link yet."
                    };
                }

                return new RenoDxMatchedInstallResult
                {
                    Success = false,
                    MatchedGameName = match.Name,
                    Notes = match.Notes,
                    ExternalUrl = externalUrl,
                    ExternalUrlLabel = match.NexusUrl != null ? "Nexus Mods" : "Discord",
                    Error = $"'{match.Name}' has no direct download — it's only distributed via {(match.NexusUrl != null ? "Nexus Mods" : "Discord")}."
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
                Notes = match.Notes,
                DownloadUrl = url,
                AddonFileName = fileName,
                FileHash = installResult.FileHash,
                RemoteContentLength = installResult.RemoteContentLength,
                RemoteLastModified = installResult.RemoteLastModified
            };
        }

        /* Re-matches the game against a fresh wiki fetch and probes the resulting URL's
           headers (no re-download) to tell "still current" from "wiki has a newer snapshot",
           mirroring RHI's InstalledModRecord.RemoteFileSize/SnapshotLastModified comparison. */
        public static async Task<RenoDxUpdateCheckResult> CheckForUpdateAsync(
            RenoDxInstalledRecord record,
            string wikiCacheDirectory,
            IDictionary<string, string> wikiNameOverrides = null)
        {
            var wikiService = new RenoDxWikiService(wikiCacheDirectory);
            var mods = await wikiService.FetchAllAsync().ConfigureAwait(false);
            var match = RenoDxGameMatcher.Match(record.GameName, mods, wikiNameOverrides);
            if (match == null || (match.SnapshotUrl == null && match.SnapshotUrl32 == null))
                return new RenoDxUpdateCheckResult { Status = RenoDxUpdateStatus.NoLongerListed };

            var preferUrl32 = record.AddonFileName != null &&
                record.AddonFileName.EndsWith("32", StringComparison.OrdinalIgnoreCase);
            var url = preferUrl32 ? (match.SnapshotUrl32 ?? match.SnapshotUrl) : (match.SnapshotUrl ?? match.SnapshotUrl32);

            var probe = await AddonInstallService.ProbeAsync(url).ConfigureAwait(false);

            var changed = !string.Equals(url, record.DownloadUrl, StringComparison.Ordinal)
                || (probe.ContentLength.HasValue && record.RemoteContentLength.HasValue && probe.ContentLength != record.RemoteContentLength)
                || (probe.LastModified.HasValue && record.RemoteLastModified.HasValue && probe.LastModified > record.RemoteLastModified);

            return new RenoDxUpdateCheckResult
            {
                Status = changed ? RenoDxUpdateStatus.UpdateAvailable : RenoDxUpdateStatus.UpToDate,
                MatchedGameName = match.Name,
                CurrentUrl = url
            };
        }

        /* Every RenoDX build AddonInstallService kept as "<addon>.previous" in the game's deploy
           directory — generic engine installs included, which have no RenoDxInstalledRecord. */
        public static List<string> FindPreviousBuilds(string gameInstallPath)
        {
            if (string.IsNullOrEmpty(gameInstallPath) || !Directory.Exists(gameInstallPath)) return new List<string>();

            try
            {
                var deployDir = PeInspector.ResolveDeployDirectory(gameInstallPath);
                return Directory.EnumerateFiles(deployDir, "renodx-*" + AddonInstallService.PreviousBuildSuffix)
                    .Where(p => p.EndsWith(".addon64" + AddonInstallService.PreviousBuildSuffix, StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".addon32" + AddonInstallService.PreviousBuildSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not look for previous RenoDX builds under '{gameInstallPath}'.");
                return new List<string>();
            }
        }

        /* Swaps each live addon with its kept previous build rather than just restoring the old
           one, so the newer build becomes the ".previous" and a second swap undoes the first. */
        public static RenoDxSwapResult SwapToPreviousBuild(string gameInstallPath)
        {
            var previousBuilds = FindPreviousBuilds(gameInstallPath);
            if (previousBuilds.Count == 0)
                return new RenoDxSwapResult { Success = false, Error = "No previous RenoDX build is kept for this game." };

            var result = new RenoDxSwapResult { Success = true };
            foreach (var previousPath in previousBuilds)
            {
                var livePath = previousPath.Substring(0, previousPath.Length - AddonInstallService.PreviousBuildSuffix.Length);
                var tempPath = livePath + ".swapping";
                try
                {
                    if (File.Exists(livePath)) File.Move(livePath, tempPath);
                    File.Move(previousPath, livePath);
                    if (File.Exists(tempPath)) File.Move(tempPath, previousPath);

                    result.SwappedFiles.Add(Path.GetFileName(livePath));
                    result.NowActiveInstalledAt = File.GetLastWriteTime(livePath);
                    Log.Info($"Swapped RenoDX '{livePath}' with its previous build.");
                }
                catch (Exception ex)
                {
                    // Put the live build back if the swap got partway.
                    if (!File.Exists(livePath) && File.Exists(tempPath))
                    {
                        try { File.Move(tempPath, livePath); } catch (Exception restoreEx) { Log.Warn(restoreEx, $"Failed to restore '{livePath}'."); }
                    }

                    Log.Error(ex, $"Failed to swap RenoDX '{livePath}' with its previous build.");
                    return new RenoDxSwapResult { Success = false, Error = $"Couldn't swap {Path.GetFileName(livePath)}: {ex.Message} (is the game still running?)" };
                }
            }

            return result;
        }

        public static RenoDxUninstallResult Uninstall(RenoDxInstalledRecord record)
        {
            try
            {
                if (File.Exists(record.InstallPath))
                    File.Delete(record.InstallPath);

                var previousPath = record.InstallPath + AddonInstallService.PreviousBuildSuffix;
                if (File.Exists(previousPath))
                    File.Delete(previousPath);

                Log.Info($"Uninstalled RenoDX addon for '{record.GameName}' from '{record.InstallPath}'.");
                return new RenoDxUninstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to delete RenoDX addon at '{record.InstallPath}'.");
                return new RenoDxUninstallResult { Success = false, Error = ex.Message };
            }
        }
    }
}
