using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.ReShade;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class DxvkInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> InstalledDlls { get; set; }
        public List<string> BackedUpFiles { get; set; }
        public bool DeployedConf { get; set; }
        public string Version { get; set; }
        /* Always set on success — DXVK is a meaningful compatibility/risk tradeoff, not a
           transparent drop-in, so this is surfaced to the user every time, not just logged. */
        public string Warning { get; set; }
    }

    public enum DxvkUpdateStatus { UpToDate, UpdateAvailable, CouldNotCheck }

    public class DxvkUpdateCheckResult
    {
        public DxvkUpdateStatus Status { get; set; }
        public string LatestVersion { get; set; }
    }

    public class DxvkUninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /* DXVK+Lilium HDR drop-in, scoped to just the Lilium HDR fork (EndlesslyFlowering/dxvk) —
       the actual HDR-relevant variant per PLAN.md. RHI also offers vanilla dev/stable DXVK
       variants (unrelated to HDR) and a legacy DX9-proxy migration path; neither is ported here.
       RHI additionally registers ReShade as a global Vulkan implicit layer (admin-elevated
       registry change) so ReShade keeps working once DXVK claims the DX proxy DLL slot — that
       registration is deliberately NOT automated here (too invasive for an unattended install
       flow); see the Warning surfaced on every successful install. */
    public static class DxvkService
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/EndlesslyFlowering/dxvk/releases/latest";
        private const string DefaultDxvkConf =
            "dxgi.enableHDR = True\ndxvk.allowFse = False\ndxvk.latencySleep = Auto\nd3d9.dpiAware = True\n";

        private static readonly HttpClient Http = CreateHttpClient();

        public static async Task<DxvkInstallResult> InstallAsync(string gameInstallPath, string cacheDirectory)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null) return Fail("No executable found in the install folder.");

            var info = PeInspector.Inspect(exePath);
            if (info == null) return Fail("Could not read the game executable's PE header.");

            string[] requiredDlls;
            switch (info.Api)
            {
                case GraphicsApi.D3D9:
                    requiredDlls = new[] { "d3d9.dll" };
                    break;
                case GraphicsApi.D3D10:
                    requiredDlls = new[] { "d3d10core.dll", "dxgi.dll" };
                    break;
                default:
                    return Fail(
                        $"DXVK+Lilium HDR isn't offered for {info.Api} — DX11 is excluded (DXVK has known " +
                        "overlay/VRR/perf issues on DX11 per upstream), and DX12/Vulkan games don't need a translation layer.");
            }

            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return Fail("Could not determine the latest Lilium HDR DXVK release.");

            var archivePath = await DownloadArchiveAsync(release, cacheDirectory).ConfigureAwait(false);
            if (archivePath == null) return Fail("Could not download the Lilium HDR DXVK archive.");

            var extractDir = Path.Combine(cacheDirectory, release.Version, "extracted");
            try
            {
                if (!Directory.Exists(extractDir) || !Directory.EnumerateFileSystemEntries(extractDir).Any())
                    SevenZipExtractor.ExtractAll(File.ReadAllBytes(archivePath), extractDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract the Lilium HDR DXVK archive.");
                return Fail($"Failed to extract archive: {ex.Message}");
            }

            var installedDlls = new List<string>();
            var backedUp = new List<string>();
            var preferredSegment = info.Is64Bit ? "x64" : "x32";

            foreach (var dll in requiredDlls)
            {
                var sourcePath = SevenZipExtractor.FindFile(extractDir, dll, preferredSegment);
                if (sourcePath == null) return Fail($"Extracted archive is missing {dll}.");

                var targetPath = Path.Combine(gameInstallPath, dll);
                if (FileBackup.BackupIfNeeded(targetPath)) backedUp.Add(dll);

                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to copy {dll} to '{targetPath}'.");
                    return Fail($"Failed to copy {dll}: {ex.Message}");
                }

                installedDlls.Add(dll);
            }

            var deployedConf = false;
            try
            {
                File.WriteAllText(Path.Combine(gameInstallPath, "dxvk.conf"), DefaultDxvkConf);
                deployedConf = true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to write dxvk.conf — DXVK will use its built-in defaults (HDR not force-enabled).");
            }

            Log.Info($"Installed Lilium HDR DXVK {release.Version} ({info.Api}) to '{gameInstallPath}'.");
            return new DxvkInstallResult
            {
                Success = true,
                InstalledDlls = installedDlls,
                BackedUpFiles = backedUp,
                DeployedConf = deployedConf,
                Version = release.Version,
                Warning = "DXVK replaces this game's rendering backend with Vulkan — this can trigger anti-cheat bans on " +
                    "protected games, break Steam/Discord/RTSS overlays, and blocks exclusive fullscreen (use borderless " +
                    "instead). If ReShade is also installed for this game, it needs to run as a global Vulkan implicit " +
                    "layer once DXVK is installed — this plugin doesn't automate that (admin-elevated registry change); " +
                    "see PLAN.md."
            };
        }

        public static async Task<DxvkUpdateCheckResult> CheckForUpdateAsync(DxvkInstalledRecord record)
        {
            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return new DxvkUpdateCheckResult { Status = DxvkUpdateStatus.CouldNotCheck };

            return new DxvkUpdateCheckResult
            {
                Status = !string.Equals(release.Version, record.Version, StringComparison.Ordinal)
                    ? DxvkUpdateStatus.UpdateAvailable
                    : DxvkUpdateStatus.UpToDate,
                LatestVersion = release.Version
            };
        }

        public static DxvkUninstallResult Uninstall(DxvkInstalledRecord record)
        {
            try
            {
                foreach (var dll in record.InstalledDlls ?? new List<string>())
                {
                    var path = Path.Combine(record.InstallPath, dll);
                    if (File.Exists(path)) File.Delete(path);

                    var backupPath = path + ".original";
                    if (record.BackedUpFiles != null && record.BackedUpFiles.Contains(dll) && File.Exists(backupPath))
                        File.Move(backupPath, path);
                }

                if (record.DeployedConf)
                {
                    var confPath = Path.Combine(record.InstallPath, "dxvk.conf");
                    if (File.Exists(confPath)) File.Delete(confPath);
                }

                Log.Info($"Uninstalled DXVK+Lilium from '{record.GameName}'.");
                return new DxvkUninstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to uninstall DXVK+Lilium for '{record.GameName}'.");
                return new DxvkUninstallResult { Success = false, Error = ex.Message };
            }
        }

        private class DxvkReleaseInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
        }

        private static async Task<DxvkReleaseInfo> GetLatestReleaseAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var tag = (string)parsed["tag_name"];
                if (string.IsNullOrEmpty(tag)) return null;

                var asset = parsed["assets"]?
                    .Select(a => new { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                    .FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                        && a.Name.IndexOf("gplasync", StringComparison.OrdinalIgnoreCase) < 0);
                if (asset == null) return null;

                return new DxvkReleaseInfo { Version = tag, DownloadUrl = asset.Url };
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to check the latest Lilium HDR DXVK release.");
                return null;
            }
        }

        private static async Task<string> DownloadArchiveAsync(DxvkReleaseInfo release, string cacheDirectory)
        {
            var versionDir = Path.Combine(cacheDirectory, release.Version);
            Directory.CreateDirectory(versionDir);
            var archivePath = Path.Combine(versionDir, "dxvk-lilium.7z");

            if (File.Exists(archivePath)) return archivePath;

            try
            {
                var bytes = await Http.GetByteArrayAsync(release.DownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(archivePath, bytes);
                return archivePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download Lilium HDR DXVK {release.Version}.");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }

        private static DxvkInstallResult Fail(string error)
        {
            Log.Error(error);
            return new DxvkInstallResult { Success = false, Error = error };
        }
    }
}
