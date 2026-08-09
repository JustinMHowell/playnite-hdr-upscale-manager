using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.ReShade;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class OptiScalerInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string DllName { get; set; }
        public List<string> InstalledFiles { get; set; }
        public bool OptiPatcherInstalled { get; set; }
        public bool DlssDllInstalled { get; set; }
        public string Version { get; set; }
    }

    public enum OptiScalerUpdateStatus { UpToDate, UpdateAvailable, CouldNotCheck }

    public class OptiScalerUpdateCheckResult
    {
        public OptiScalerUpdateStatus Status { get; set; }
        public string LatestVersion { get; set; }
    }

    public class OptiScalerUninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /* OptiScaler AMD install — FSR/XeSS substitution for DLSS-only titles, the single most
       AMD-relevant feature in this whole addon (see PLAN.md §3). Scoped from RHI's
       OptiScalerService: NVIDIA's own INI template/branch is dropped entirely (Phase 4/NVIDIA is
       out of scope, see PLAN.md §6), and DllOverrideConfig's per-game *user* override (RHI lets a
       user pick any of 7 proxy names via UI) isn't ported — only the manifest override + the
       Vulkan auto-override are, which covers the two real-world cases (a game that needs a
       specific proxy name, and Vulkan games needing winmm.dll instead of dxgi.dll). */
    public static class OptiScalerService
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest";
        private const string OptiPatcherReleaseApiUrl = "https://api.github.com/repos/optiscaler/OptiPatcher/releases/tags/rolling";
        private const string DlssManifestUrl = "https://raw.githubusercontent.com/beeradmoore/dlss-swapper-manifest-builder/main/manifest.json";
        private const string IniResourceName = "HdrUpscaleManager.Resources.OptiScaler.ini.template";

        /* Fixed filename OptiScaler's own LoadReshade setting looks for (see DeployIni) — RHI
           uses this same literal name regardless of the game's bitness. */
        public const string ReShadeCoexistName = "ReShade64.dll";

        private static readonly HashSet<string> SkipTopLevelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "version.txt", "OptiScaler.dll", "OptiScaler.ini"
        };
        private static readonly HashSet<string> SkipTopLevelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".sh", ".txt"
        };

        private static readonly HttpClient Http = CreateHttpClient();

        public static string ResolveDllName(string gameName, GraphicsApi api, RemoteManifest manifest)
        {
            if (manifest?.OptiScalerDllOverrides != null &&
                manifest.OptiScalerDllOverrides.TryGetValue(gameName, out var overrideName) &&
                !string.IsNullOrEmpty(overrideName))
                return overrideName;

            /* dxgi.dll won't load under Vulkan — the one vendor-agnostic auto-override RHI applies. */
            return api == GraphicsApi.Vulkan ? "winmm.dll" : "dxgi.dll";
        }

        /* OptiScaler.ini is always deployed under this exact literal name (see DeployIni) and
           never renamed, unlike OptiScaler.dll itself — the one reliable file-based marker that
           doesn't depend on which proxy DLL name was chosen for this game. */
        public static bool IsInstalled(string gameInstallPath) =>
            File.Exists(Path.Combine(gameInstallPath, "OptiScaler.ini"));

        public static async Task<OptiScalerInstallResult> InstallAsync(
            string gameInstallPath,
            string gameName,
            bool dlssInputs,
            string cacheDirectory,
            RemoteManifest manifest = null,
            bool installOptiPatcher = true,
            bool installDlssDll = true)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null) return Fail("No executable found in the install folder.");

            var info = PeInspector.Inspect(exePath);
            if (info == null) return Fail("Could not read the game executable's PE header.");

            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return Fail("Could not determine the latest OptiScaler release.");

            string stagingRoot;
            try
            {
                var stagingDir = Path.Combine(cacheDirectory, release.Version, "staged");
                if (!Directory.Exists(stagingDir) || !Directory.EnumerateFileSystemEntries(stagingDir).Any())
                {
                    var archivePath = await DownloadArchiveAsync(release, cacheDirectory).ConfigureAwait(false);
                    if (archivePath == null) return Fail("Could not download the OptiScaler archive.");
                    SevenZipExtractor.ExtractAll(File.ReadAllBytes(archivePath), stagingDir);
                }

                var dllSourcePath = SevenZipExtractor.FindFile(stagingDir, "OptiScaler.dll");
                if (dllSourcePath == null) return Fail("Extracted OptiScaler archive is missing OptiScaler.dll.");
                stagingRoot = Path.GetDirectoryName(dllSourcePath) ?? stagingDir;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract the OptiScaler archive.");
                return Fail($"Failed to extract archive: {ex.Message}");
            }

            var dllName = ResolveDllName(gameName, info.Api, manifest);
            var installedFiles = new List<string>();

            /* OptiScaler and ReShade can't both claim the same proxy DLL name. If ReShade is
               already sitting in the slot OptiScaler wants, move it to the fixed name
               OptiScaler's own LoadReshade setting expects (see DeployIni) instead of letting
               FileBackup treat it as "the game's original file" and back it up/clobber it. */
            if (ShouldDisplaceReShade(gameInstallPath, dllName, info.Api))
                MoveReShadeToCoexistName(gameInstallPath, dllName);

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(stagingRoot))
                {
                    var name = Path.GetFileName(filePath);
                    if (SkipTopLevelFiles.Contains(name)) continue;
                    if (SkipTopLevelExtensions.Contains(Path.GetExtension(name))) continue;

                    var destPath = Path.Combine(gameInstallPath, name);
                    FileBackup.BackupIfNeeded(destPath);
                    File.Copy(filePath, destPath, overwrite: true);
                    installedFiles.Add(name);
                }

                foreach (var dirPath in Directory.EnumerateDirectories(stagingRoot))
                {
                    var dirName = Path.GetFileName(dirPath);
                    if (string.Equals(dirName, "Licenses", StringComparison.OrdinalIgnoreCase)) continue;

                    CopyDirectoryRecursive(dirPath, Path.Combine(gameInstallPath, dirName));
                    installedFiles.Add(dirName + Path.DirectorySeparatorChar);
                }

                var targetDllPath = Path.Combine(gameInstallPath, dllName);
                FileBackup.BackupIfNeeded(targetDllPath);
                File.Copy(Path.Combine(stagingRoot, "OptiScaler.dll"), targetDllPath, overwrite: true);
                installedFiles.Add(dllName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deploy OptiScaler files for '{gameName}'.");
                return Fail($"Failed to install: {ex.Message}");
            }

            try
            {
                DeployIni(gameInstallPath, dlssInputs);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to deploy OptiScaler.ini — OptiScaler will run with its built-in defaults.");
            }

            var optiPatcherInstalled = installOptiPatcher && await TryInstallOptiPatcherAsync(gameInstallPath).ConfigureAwait(false);
            var dlssDllInstalled = installDlssDll && await TryInstallDlssDllAsync(gameInstallPath, cacheDirectory).ConfigureAwait(false);

            Log.Info($"Installed OptiScaler {release.Version} ({dllName}) to '{gameInstallPath}'.");
            return new OptiScalerInstallResult
            {
                Success = true,
                DllName = dllName,
                InstalledFiles = installedFiles,
                OptiPatcherInstalled = optiPatcherInstalled,
                DlssDllInstalled = dlssDllInstalled,
                Version = release.Version
            };
        }

        /* True when ReShade is already installed under the exact proxy name OptiScaler is about
           to claim for itself — the one case that actually needs the coexistence dance below. */
        internal static bool ShouldDisplaceReShade(string gameInstallPath, string dllName, GraphicsApi api)
        {
            var reShadeProxyName = ReShadeProxyDll.GetProxyDllName(api);
            return string.Equals(reShadeProxyName, dllName, StringComparison.OrdinalIgnoreCase) &&
                ReShadeInstallService.IsInstalled(gameInstallPath, api);
        }

        internal static void MoveReShadeToCoexistName(string gameInstallPath, string reShadeCurrentName)
        {
            var currentPath = Path.Combine(gameInstallPath, reShadeCurrentName);
            var coexistPath = Path.Combine(gameInstallPath, ReShadeCoexistName);
            if (!File.Exists(currentPath) || File.Exists(coexistPath)) return;

            try
            {
                File.Move(currentPath, coexistPath);
                Log.Info($"Moved ReShade '{reShadeCurrentName}' to '{ReShadeCoexistName}' so OptiScaler can use '{reShadeCurrentName}'.");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to move ReShade out of the way at '{currentPath}' — OptiScaler install will back it up instead.");
            }
        }

        /* Undoes MoveReShadeToCoexistName (or a ReShade install that deployed straight to the
           coexist name because OptiScaler was already present) — moves ReShade back to its
           normal proxy name now that OptiScaler's own file is gone. Leaves it as
           ReShadeCoexistName if that name is occupied by something else; still functional via
           LoadReshade, just not renamed back.

           restoreName is record.DllName rather than a fresh PE re-inspection: coexistence only
           ever triggers when ReShade's proxy name matches OptiScaler's own resolved dllName (see
           InstallAsync and ReShadeInstallService.InstallAsync), so the slot OptiScaler is about
           to vacate is always the correct name to hand ReShade back. */
        internal static void RestoreReShadeFromCoexistName(string gameInstallPath, string restoreName)
        {
            var coexistPath = Path.Combine(gameInstallPath, ReShadeCoexistName);
            if (!File.Exists(coexistPath) || string.IsNullOrEmpty(restoreName)) return;

            var restorePath = Path.Combine(gameInstallPath, restoreName);
            if (File.Exists(restorePath))
            {
                Log.Warn($"Can't restore ReShade to '{restoreName}' — occupied. Leaving it as '{ReShadeCoexistName}'.");
                return;
            }

            try
            {
                File.Move(coexistPath, restorePath);
                Log.Info($"Restored ReShade '{ReShadeCoexistName}' back to '{restoreName}'.");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to restore ReShade from '{ReShadeCoexistName}' to '{restoreName}'.");
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(sourceDir))
                CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        internal static void DeployIni(string gameInstallPath, bool dlssInputs)
        {
            var targetIniPath = Path.Combine(gameInstallPath, "OptiScaler.ini");
            /* Preserve an existing per-game INI across installs/updates, matching RHI. */
            if (File.Exists(targetIniPath)) return;

            var content = LoadIniTemplate();
            if (!dlssInputs)
                content = ReplaceExactLine(content, "Dxgi=auto", "Dxgi=false");

            content = ReplaceExactLine(content, "LoadReshade=auto", "LoadReshade=true");
            content = ReplaceExactLine(content, "LoadAsiPlugins=auto", "LoadAsiPlugins=true");
            content = ReplaceExactLine(content, "ShortcutKey=auto", "ShortcutKey=0x2D");
            /* Off by default (see OptiScaler.ini's own LogToFile comment) — forced on so
               OptiScaler.log exists to confirm a real launch actually loaded it (and, via
               LoadReshade above, chain-loaded ReShade too). */
            content = ReplaceExactLine(content, "LogToFile=auto", "LogToFile=true");

            File.WriteAllText(targetIniPath, content.Replace("\n", Environment.NewLine));
        }

        /* Several INI keys share the literal value "=auto" — a blind string replace would touch
           unrelated settings, so this only replaces a line whose trimmed text matches exactly. */
        internal static string ReplaceExactLine(string content, string exactLine, string replacement)
        {
            var lines = content.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != exactLine) continue;
                lines[i] = replacement;
                break;
            }

            return string.Join("\n", lines);
        }

        private static string LoadIniTemplate()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(IniResourceName))
            {
                if (stream == null) throw new InvalidOperationException($"Embedded resource '{IniResourceName}' not found.");
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /* OptiPatcher (AMD/Intel DX11-on-12 compatibility patch) — best-effort, install proceeds
           without it on failure since the core upscaler substitution doesn't depend on it. */
        private static async Task<bool> TryInstallOptiPatcherAsync(string gameInstallPath)
        {
            try
            {
                var json = await Http.GetStringAsync(OptiPatcherReleaseApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var asset = (parsed["assets"] as JArray)?
                    .Select(a => new { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                    .FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    Log.Warn("OptiPatcher's 'rolling' release has no .asi asset.");
                    return false;
                }

                var bytes = await Http.GetByteArrayAsync(asset.Url).ConfigureAwait(false);
                var pluginsDir = Path.Combine(gameInstallPath, "plugins");
                Directory.CreateDirectory(pluginsDir);
                File.WriteAllBytes(Path.Combine(pluginsDir, "OptiPatcher.asi"), bytes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to install OptiPatcher — continuing without it.");
                return false;
            }
        }

        /* The DLSS-input channel: OptiScaler intercepts calls meant for nvngx_dlss.dll and
           substitutes FSR/XeSS, but the physical DLL must be present first (an INI path override
           alone isn't reliable — see OptiScaler's own install notes). Sourced from the
           community-run dlss-swapper manifest since NVIDIA doesn't publish standalone downloads.
           Best-effort: DLSS-only titles just won't be substitutable without it, everything else
           OptiScaler does still works. */
        private static async Task<bool> TryInstallDlssDllAsync(string gameInstallPath, string cacheDirectory)
        {
            try
            {
                var json = await Http.GetStringAsync(DlssManifestUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var entries = parsed["dlss"] as JArray;
                if (entries == null || entries.Count == 0) return false;

                var best = entries
                    .Where(e => (bool?)e["is_dev_file"] != true)
                    .OrderByDescending(e => (long?)e["version_number"] ?? 0)
                    .FirstOrDefault();
                var downloadUrl = (string)best?["download_url"];
                if (string.IsNullOrEmpty(downloadUrl)) return false;

                var cacheDir = Path.Combine(cacheDirectory, "dlss-dll");
                Directory.CreateDirectory(cacheDir);
                var zipPath = Path.Combine(cacheDir, Path.GetFileName(new Uri(downloadUrl).LocalPath));
                if (!File.Exists(zipPath))
                {
                    var bytes = await Http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
                    File.WriteAllBytes(zipPath, bytes);
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return false;
                    entry.ExtractToFile(Path.Combine(gameInstallPath, "nvngx_dlss.dll"), overwrite: true);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to stage the DLSS DLL — DLSS-only titles may not be substitutable without it.");
                return false;
            }
        }

        public static async Task<OptiScalerUpdateCheckResult> CheckForUpdateAsync(OptiScalerInstalledRecord record)
        {
            var release = await GetLatestReleaseAsync().ConfigureAwait(false);
            if (release == null) return new OptiScalerUpdateCheckResult { Status = OptiScalerUpdateStatus.CouldNotCheck };

            return new OptiScalerUpdateCheckResult
            {
                Status = !string.Equals(release.Version, record.Version, StringComparison.Ordinal)
                    ? OptiScalerUpdateStatus.UpdateAvailable
                    : OptiScalerUpdateStatus.UpToDate,
                LatestVersion = release.Version
            };
        }

        public static OptiScalerUninstallResult Uninstall(OptiScalerInstalledRecord record)
        {
            try
            {
                foreach (var relative in record.InstalledFiles ?? new List<string>())
                {
                    var isDirectory = relative.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
                    var fullPath = Path.Combine(record.InstallPath, relative.TrimEnd(Path.DirectorySeparatorChar));

                    if (isDirectory)
                    {
                        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
                        continue;
                    }

                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    var backupPath = fullPath + ".original";
                    if (File.Exists(backupPath)) File.Move(backupPath, fullPath);
                }

                if (record.OptiPatcherInstalled)
                {
                    var asiPath = Path.Combine(record.InstallPath, "plugins", "OptiPatcher.asi");
                    if (File.Exists(asiPath)) File.Delete(asiPath);
                }

                if (record.DlssDllInstalled)
                {
                    var dlssPath = Path.Combine(record.InstallPath, "nvngx_dlss.dll");
                    if (File.Exists(dlssPath)) File.Delete(dlssPath);
                }

                RestoreReShadeFromCoexistName(record.InstallPath, record.DllName);

                Log.Info($"Uninstalled OptiScaler from '{record.GameName}'.");
                return new OptiScalerUninstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to uninstall OptiScaler for '{record.GameName}'.");
                return new OptiScalerUninstallResult { Success = false, Error = ex.Message };
            }
        }

        private class OptiScalerReleaseInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
        }

        private static async Task<OptiScalerReleaseInfo> GetLatestReleaseAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var tag = (string)parsed["tag_name"];
                if (string.IsNullOrEmpty(tag)) return null;

                var asset = (parsed["assets"] as JArray)?
                    .Select(a => new { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                    .FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
                if (asset == null) return null;

                return new OptiScalerReleaseInfo { Version = tag, DownloadUrl = asset.Url };
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to check the latest OptiScaler release.");
                return null;
            }
        }

        private static async Task<string> DownloadArchiveAsync(OptiScalerReleaseInfo release, string cacheDirectory)
        {
            var versionDir = Path.Combine(cacheDirectory, release.Version);
            Directory.CreateDirectory(versionDir);
            var archivePath = Path.Combine(versionDir, "optiscaler.7z");

            if (File.Exists(archivePath)) return archivePath;

            try
            {
                var bytes = await Http.GetByteArrayAsync(release.DownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(archivePath, bytes);
                return archivePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download OptiScaler {release.Version}.");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }

        private static OptiScalerInstallResult Fail(string error)
        {
            Log.Error(error);
            return new OptiScalerInstallResult { Success = false, Error = error };
        }
    }
}
