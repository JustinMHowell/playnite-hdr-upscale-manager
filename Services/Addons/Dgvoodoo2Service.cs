using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.ReShade;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class Dgvoodoo2InstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> InstalledFiles { get; set; }
        public List<string> BackedUpFiles { get; set; }
        /* The real directory files were deployed to — may differ from the gameInstallPath
           InstallAsync was called with (see PeInspector.ResolveDeployDirectory). */
        public string DeployDirectory { get; set; }
        public string Version { get; set; }
    }

    public enum Dgvoodoo2UpdateStatus { UpToDate, UpdateAvailable, CouldNotCheck }

    public class Dgvoodoo2UpdateCheckResult
    {
        public Dgvoodoo2UpdateStatus Status { get; set; }
        public string LatestVersion { get; set; }
    }

    public class Dgvoodoo2UninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /* dgVoodoo2 — a DirectDraw/Direct3D-version-upgrade wrapper (DX1-9 up to a real D3D11
       device) — needed as a Luma prerequisite for pre-DX11 games whose Luma shader hooks only
       target DX11. Confirmed live 2026-08-09 against Vanquish (DX9, 32-bit): without it, the
       game fails at IDirectInput8A::CreateDevice before ever reaching D3D; with MS/x86/D3D9.dll
       + dgVoodoo.conf dropped into its real deploy directory, it reaches a real D3D11 device and
       Luma's shaders compile. Downloaded from the developer's own GitHub releases
       (dege-diosg/dgVoodoo2) — same "download at install time, redistribute nothing" principle
       as every other component here. See LumaPrerequisitesManifest for which games need this. */
    public static class Dgvoodoo2Service
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/dege-diosg/dgVoodoo2/releases/latest";

        private static readonly HttpClient Http = CreateHttpClient();

        public static async Task<Dgvoodoo2InstallResult> InstallAsync(string gameInstallPath, string cacheDirectory, GraphicsApi? apiOverride = null)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null) return Fail("No executable found in the install folder.");

            var info = PeInspector.Inspect(exePath);
            if (info == null) return Fail("Could not read the game executable's PE header.");

            /* Deploy next to the real exe, not gameInstallPath itself — see
               PeInspector.ResolveDeployDirectory's own doc comment for why. */
            var deployDir = Path.GetDirectoryName(exePath) ?? gameInstallPath;

            var api = apiOverride ?? info.Api;
            string dllName;
            switch (api)
            {
                case GraphicsApi.D3D8: dllName = "D3D8.dll"; break;
                case GraphicsApi.D3D9: dllName = "D3D9.dll"; break;
                default:
                    return Fail($"dgVoodoo2 isn't offered for {api} — it only wraps DirectDraw/Direct3D up to version 9.");
            }

            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return Fail("Could not determine the latest dgVoodoo2 release.");

            var archivePath = await DownloadArchiveAsync(release, cacheDirectory).ConfigureAwait(false);
            if (archivePath == null) return Fail("Could not download the dgVoodoo2 archive.");

            var extractDir = Path.Combine(cacheDirectory, release.Version, "extracted");
            try
            {
                if (!Directory.Exists(extractDir) || !Directory.EnumerateFileSystemEntries(extractDir).Any())
                {
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(archivePath, extractDir);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract the dgVoodoo2 archive.");
                return Fail($"Failed to extract archive: {ex.Message}");
            }

            // Zip layout confirmed live 2026-08-09 (v2.87.3): MS/x86/D3D9.dll, MS/x64/D3D9.dll,
            // MS/x86/D3D8.dll, dgVoodoo.conf at the archive root.
            var bitnessFolder = info.Is64Bit ? "x64" : "x86";
            var sourceDllPath = Path.Combine(extractDir, "MS", bitnessFolder, dllName);
            if (!File.Exists(sourceDllPath))
                return Fail($"Extracted dgVoodoo2 archive is missing MS/{bitnessFolder}/{dllName}.");

            var installedFiles = new List<string>();
            var backedUp = new List<string>();

            var targetDllPath = Path.Combine(deployDir, dllName);
            if (FileBackup.BackupIfNeeded(targetDllPath)) backedUp.Add(dllName);

            try
            {
                File.Copy(sourceDllPath, targetDllPath, overwrite: true);
                installedFiles.Add(dllName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to copy {dllName} to '{targetDllPath}'.");
                return Fail($"Failed to copy {dllName}: {ex.Message}");
            }

            var sourceConfPath = Path.Combine(extractDir, "dgVoodoo.conf");
            if (File.Exists(sourceConfPath))
            {
                var targetConfPath = Path.Combine(deployDir, "dgVoodoo.conf");
                if (FileBackup.BackupIfNeeded(targetConfPath)) backedUp.Add("dgVoodoo.conf");

                try
                {
                    File.Copy(sourceConfPath, targetConfPath, overwrite: true);
                    installedFiles.Add("dgVoodoo.conf");
                }
                catch (Exception ex)
                {
                    // Not fatal — dgVoodoo2 falls back to its own built-in defaults without this.
                    Log.Warn(ex, "Failed to copy dgVoodoo.conf — dgVoodoo2 will use its own built-in defaults.");
                }
            }

            Log.Info($"Installed dgVoodoo2 {release.Version} ({dllName}) to '{deployDir}'.");
            return new Dgvoodoo2InstallResult
            {
                Success = true,
                InstalledFiles = installedFiles,
                BackedUpFiles = backedUp,
                DeployDirectory = deployDir,
                Version = release.Version
            };
        }

        public static async Task<Dgvoodoo2UpdateCheckResult> CheckForUpdateAsync(Dgvoodoo2InstalledRecord record)
        {
            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return new Dgvoodoo2UpdateCheckResult { Status = Dgvoodoo2UpdateStatus.CouldNotCheck };

            return new Dgvoodoo2UpdateCheckResult
            {
                Status = !string.Equals(release.Version, record.Version, StringComparison.Ordinal)
                    ? Dgvoodoo2UpdateStatus.UpdateAvailable
                    : Dgvoodoo2UpdateStatus.UpToDate,
                LatestVersion = release.Version
            };
        }

        public static Dgvoodoo2UninstallResult Uninstall(Dgvoodoo2InstalledRecord record)
        {
            try
            {
                foreach (var file in record.InstalledFiles ?? new List<string>())
                {
                    var path = Path.Combine(record.InstallPath, file);
                    if (File.Exists(path)) File.Delete(path);

                    var backupPath = path + ".original";
                    if (record.BackedUpFiles != null && record.BackedUpFiles.Contains(file) && File.Exists(backupPath))
                        File.Move(backupPath, path);
                }

                Log.Info($"Uninstalled dgVoodoo2 from '{record.GameName}'.");
                return new Dgvoodoo2UninstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to uninstall dgVoodoo2 for '{record.GameName}'.");
                return new Dgvoodoo2UninstallResult { Success = false, Error = ex.Message };
            }
        }

        private class Dgvoodoo2ReleaseInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
        }

        private static async Task<Dgvoodoo2ReleaseInfo> GetLatestReleaseAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var tag = (string)parsed["tag_name"];
                if (string.IsNullOrEmpty(tag)) return null;

                // The plain release zip only — the same release also publishes _dbg (debug
                // symbols) and _dev64 (SDK) variants as separate assets.
                var asset = parsed["assets"]?
                    .Select(a => new { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                    .FirstOrDefault(a => a.Name != null
                        && a.Name.StartsWith("dgVoodoo2_", StringComparison.OrdinalIgnoreCase)
                        && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && a.Name.IndexOf("_dbg", StringComparison.OrdinalIgnoreCase) < 0
                        && a.Name.IndexOf("_dev64", StringComparison.OrdinalIgnoreCase) < 0);
                if (asset == null) return null;

                return new Dgvoodoo2ReleaseInfo { Version = tag, DownloadUrl = asset.Url };
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to check the latest dgVoodoo2 release.");
                return null;
            }
        }

        private static async Task<string> DownloadArchiveAsync(Dgvoodoo2ReleaseInfo release, string cacheDirectory)
        {
            var versionDir = Path.Combine(cacheDirectory, release.Version);
            Directory.CreateDirectory(versionDir);
            var archivePath = Path.Combine(versionDir, "dgvoodoo2.zip");

            if (File.Exists(archivePath)) return archivePath;

            try
            {
                var bytes = await Http.GetByteArrayAsync(release.DownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(archivePath, bytes);
                return archivePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download dgVoodoo2 {release.Version}.");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }

        private static Dgvoodoo2InstallResult Fail(string error)
        {
            Log.Error(error);
            return new Dgvoodoo2InstallResult { Success = false, Error = error };
        }
    }
}
