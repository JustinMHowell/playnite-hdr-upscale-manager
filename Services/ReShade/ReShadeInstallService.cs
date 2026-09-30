using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Services.Addons;

namespace HdrUpscaleManager.Services.ReShade
{
    public class ReShadeInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledDllPath { get; set; }
        public string DeployDirectory { get; set; }
        public GraphicsApi Api { get; set; }
        public string Version { get; set; }
    }

    public class ReShadeUninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /* Installer download + extraction mechanism observed in RHI's ReShadeUpdateService:
       download the installer exe, extract the DLL without ever running it, rename/copy it
       into the game folder based on the detected graphics API. */
    public class ReShadeInstallService
    {
        private static readonly HttpClient Http = new HttpClient();

        private readonly string cacheDirectory;

        public ReShadeInstallService(string cacheDirectory)
        {
            this.cacheDirectory = cacheDirectory;
            Directory.CreateDirectory(cacheDirectory);
        }

        /* No-network check for RenoDX/Luma install flows that need ReShade present as a host
           first (see PLAN.md — RenoDX/Luma are themselves ReShade addons, ReShade alone isn't
           an HDR feature). Just checks whether the proxy DLL a ReShade install would use is
           already sitting in the game folder — doesn't verify it's actually ReShade specifically
           (vs. DXVK's own same-named DLL, or the game's real one), same ambiguity InstallAsync
           itself already lives with via its BackupIfNeeded rename-not-overwrite step.

           apiOverride (from RemoteManifest.GraphicsApiOverrides) skips PE import-table
           inspection for the API itself when the caller already knows it — see InstallAsync's
           own doc comment for why that inspection can't be trusted alone.

           Also checks OptiScalerService.ReShadeCoexistName — when OptiScaler is installed for
           the same game, ReShade lives there instead of under its normal proxy name (see
           InstallAsync below), and this needs to keep recognizing it as installed regardless
           of which name it currently occupies.

           Checks the real deploy directory (see PeInspector.ResolveDeployDirectory), not
           gameInstallPath itself — a proxy DLL only loads from the same folder as the actual
           running exe, which can be a subfolder of gameInstallPath (confirmed live on Deep Rock
           Galactic 2026-08-09: FSD.exe at the root is a launcher stub, the real exe is
           FSD\Binaries\Win64\FSD-Win64-Shipping.exe).

           Once OptiScaler is present, it claims the normal proxy name for itself, so a file
           existing there is OptiScaler's own, not ReShade's — checking it anyway is a false
           positive. Confirmed live 2026-08-09: this exact ambiguity made
           EnsureReShadeInstalledForRenoDxAsync think ReShade was already installed for Deep Rock
           Galactic (OptiScaler's own dxgi.dll satisfied the naive existence check) and skip
           installing it at all, leaving RenoDX's addon with no real host to load it. Once
           OptiScaler is confirmed present, ReShadeCoexistName is the only name that can actually
           mean ReShade. */
        public static bool IsInstalled(string gameInstallPath, GraphicsApi? apiOverride = null)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null) return false;

            var deployDir = Path.GetDirectoryName(exePath) ?? gameInstallPath;

            if (OptiScalerService.IsInstalled(deployDir))
                return File.Exists(Path.Combine(deployDir, OptiScalerService.ReShadeCoexistName));

            if (FindInstalledProxyDll(exePath, deployDir, apiOverride) != null)
                return true;

            return File.Exists(Path.Combine(deployDir, OptiScalerService.ReShadeCoexistName));
        }

        /* Every file name a drop-in ReShade install can live under — whatever
           ReShadeProxyDll.GetProxyDllName can return, plus the d3d10/d3d11 names ReShade's own
           setup tool also offers for manual installs. */
        private static readonly string[] CandidateProxyDllNames =
        {
            "dxgi.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "opengl32.dll",
        };

        /* Resolves which proxy DLL in deployDir is ReShade. The API-matched name is trusted on
           existence alone, same as before. Every other candidate name must also be verified as
           ReShade (see LooksLikeReShade), since dxgi.dll/d3d11.dll may just as well be DXVK's or
           the game's own. That second pass is what lets a manual ReShade install be recognized
           at all when PE inspection reads the API as GraphicsApi.Unknown — confirmed across
           multiple games, which previously only ever checked ReShadeCoexistName in that case
           and so sent EnsureReShadeInstalledForRenoDxAsync into a reinstall that then failed. */
        private static string FindInstalledProxyDll(string exePath, string deployDir, GraphicsApi? apiOverride)
        {
            var api = apiOverride ?? PeInspector.Inspect(exePath)?.Api;
            var expectedName = api != null ? ReShadeProxyDll.GetProxyDllName(api.Value) : null;
            if (expectedName != null)
            {
                var expectedPath = Path.Combine(deployDir, expectedName);
                if (File.Exists(expectedPath)) return expectedPath;
            }

            foreach (var name in CandidateProxyDllNames)
            {
                if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;

                var path = Path.Combine(deployDir, name);
                if (File.Exists(path) && LooksLikeReShade(path)) return path;
            }

            return null;
        }

        /* ReShade's own DLL carries "ReShade" in its version resource — that alone settles it.
           ReShade.ini beside it is the fallback: ReShade writes it on first launch, and its setup
           tool writes it on a manual install, while DXVK and the game's own DLLs never do. */
        internal static bool LooksLikeReShade(string dllPath)
        {
            if (IsReShadeBinary(dllPath)) return true;

            var dir = Path.GetDirectoryName(dllPath);
            return dir != null && File.Exists(Path.Combine(dir, "ReShade.ini"));
        }

        /* Version-resource check only — no ReShade.ini fallback, for when the answer decides
           whether a file gets backed up rather than just whether ReShade counts as installed. */
        private static bool IsReShadeBinary(string dllPath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(dllPath);
                return (info.ProductName?.IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (info.FileDescription?.IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not read version info for '{dllPath}'.");
                return false;
            }
        }

        /* OptiScaler already claims proxyDllName for itself when present — deploy under the
           fixed name its own LoadReshade setting looks for instead of colliding with it. */
        internal static string ResolveTargetDllName(string gameInstallPath, string proxyDllName) =>
            OptiScalerService.IsInstalled(gameInstallPath) ? OptiScalerService.ReShadeCoexistName : proxyDllName;

        /* Always installs whatever version reshade.me currently serves — there's no version
           picker yet, this will become configurable later.

           apiOverride lets a caller supply a manifest-curated GraphicsApi (RemoteManifest.
           GraphicsApiOverrides) instead of trusting PE import-table inspection alone — confirmed
           live 2026-08-08 that the hand-rolled import walk can read a real game's PE header fine
           (bitness came back correct) while still failing to recognize any graphics DLL import
           at all, for reasons not fully understood (likely a bound-import/delay-load layout this
           simple walk doesn't follow) — The Witcher 3's own bin\x64_dx12\witcher3.exe is a
           confirmed real-world case. Bitness still always comes from the real PE header below,
           since that part has never been observed to fail. */
        public async Task<ReShadeInstallResult> InstallAsync(string gameInstallPath, GraphicsApi? apiOverride = null)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null)
                return Fail("No executable found in the install folder.");

            var info = PeInspector.Inspect(exePath);
            if (info == null)
                return Fail("Could not read the game executable's PE header.");

            /* Deploy next to the real exe, not gameInstallPath itself — see
               PeInspector.ResolveDeployDirectory's own doc comment for why. */
            var deployDir = Path.GetDirectoryName(exePath) ?? gameInstallPath;

            var api = apiOverride ?? info.Api;
            var proxyDllName = ReShadeProxyDll.GetProxyDllName(api);

            /* Unknown means nothing (import table, manifest override, PCGamingWiki) could say
               which API this game uses — not that it uses one ReShade can't hook. dxgi.dll covers
               D3D10/11/12, which is nearly every game RenoDX targets, so it's a far better bet
               than refusing outright. Confirmed across multiple games whose PE imports never
               name a graphics DLL at all (see this method's own doc comment). */
            if (proxyDllName == null && api == GraphicsApi.Unknown)
            {
                proxyDllName = "dxgi.dll";
                Log.Warn($"Graphics API not detected for '{exePath}' — defaulting ReShade to dxgi.dll (D3D10/11/12).");
            }

            if (proxyDllName == null)
                return Fail($"{api} isn't supported by a drop-in ReShade install.");

            proxyDllName = ResolveTargetDllName(deployDir, proxyDllName);

            var versionInfo = await ReShadeVersionChecker.CheckLatestAsync().ConfigureAwait(false);
            if (versionInfo == null)
            {
                // reshade.me is unreachable (offline, or the site changed) — fall back to the
                // newest version we've already downloaded before, rather than failing outright
                // when a perfectly usable cached installer is sitting right there.
                versionInfo = FindNewestCachedVersion();
                if (versionInfo == null)
                    return Fail("Could not determine the latest ReShade version, and no cached installer is available offline.");

                Log.Warn($"Could not reach reshade.me — using cached ReShade {versionInfo.Version} instead of checking for a newer version.");
            }

            var installerPath = await DownloadInstallerAsync(versionInfo).ConfigureAwait(false);
            if (installerPath == null)
                return Fail("Could not download the ReShade installer.");

            var extractDir = Path.Combine(cacheDirectory, versionInfo.Version, "extracted");
            var extracted = ReShadeArchiveExtractor.ExtractFiles(installerPath, extractDir, "ReShade64.dll", "ReShade32.dll");
            if (!extracted)
                return Fail("Could not extract ReShade from the installer.");

            var extractedDllName = info.Is64Bit ? "ReShade64.dll" : "ReShade32.dll";
            var sourceDll = Path.Combine(extractDir, extractedDllName);
            if (!File.Exists(sourceDll))
                return Fail($"Extracted {extractedDllName} not found.");

            var targetPath = Path.Combine(deployDir, proxyDllName);
            BackupIfNeeded(targetPath);

            try
            {
                File.Copy(sourceDll, targetPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to copy ReShade DLL to '{targetPath}'.");
                return Fail($"Failed to copy ReShade into the game folder: {ex.Message}");
            }

            Log.Info($"Installed ReShade {versionInfo.Version} ({api}, {(info.Is64Bit ? "64-bit" : "32-bit")}) to '{targetPath}'.");
            return new ReShadeInstallResult
            {
                Success = true,
                InstalledDllPath = targetPath,
                DeployDirectory = deployDir,
                Api = api,
                Version = versionInfo.Version
            };
        }

        /* No installed-record backs this (unlike RenoDX/Luma/DXVK/OptiScaler) — see IsInstalled's
           own doc comment for why ReShade tracking works differently. Re-detects the deployed DLL
           the same way IsInstalled does, deletes it, and restores the pre-install backup
           (BackupIfNeeded's own ".original" rename) if one exists. */
        public static ReShadeUninstallResult Uninstall(string gameInstallPath, GraphicsApi? apiOverride = null)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null) return FailUninstall("No executable found in the install folder.");

            var deployDir = Path.GetDirectoryName(exePath) ?? gameInstallPath;

            if (OptiScalerService.IsInstalled(deployDir))
                return FailUninstall(
                    $"OptiScaler is present in '{deployDir}' ({Path.GetFileName(OptiScalerService.FindProxyDll(deployDir))} + OptiScaler.ini) and is " +
                    "hosting ReShade — uninstall OptiScaler first. If this add-on shows OptiScaler as not installed, " +
                    "those files weren't installed by it (or are left over) and need removing by hand.");

            /* Falls back to ReShadeCoexistName the same way IsInstalled does — ReShade stays
               under that name if OptiScaler goes away without renaming it back. */
            var targetPath = FindInstalledProxyDll(exePath, deployDir, apiOverride);
            var coexistPath = Path.Combine(deployDir, OptiScalerService.ReShadeCoexistName);
            if (targetPath == null && File.Exists(coexistPath))
                targetPath = coexistPath;
            if (targetPath == null)
                return FailUninstall("ReShade doesn't appear to be installed.");

            try
            {
                File.Delete(targetPath);

                var backupPath = targetPath + ".original";
                if (File.Exists(backupPath)) File.Move(backupPath, targetPath);

                Log.Info($"Uninstalled ReShade from '{targetPath}'.");
                return new ReShadeUninstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to uninstall ReShade from '{targetPath}'.");
                return new ReShadeUninstallResult { Success = false, Error = ex.Message };
            }
        }

        private static ReShadeUninstallResult FailUninstall(string error)
        {
            Log.Error(error);
            return new ReShadeUninstallResult { Success = false, Error = error };
        }

        /* Cache layout is cacheDirectory/<version>/ReShade_Setup_<version>_Addon.exe (see
           DownloadInstallerAsync) — folder names double as version strings, so this just needs
           to find the highest one that still has a valid installer inside. Url is left blank:
           DownloadInstallerAsync's own cache-hit check finds the file by path before ever
           looking at Url, so nothing tries to re-download using it. */
        private ReShadeVersionInfo FindNewestCachedVersion()
        {
            Version best = null;
            string bestRaw = null;

            IEnumerable<string> versionDirs;
            try { versionDirs = Directory.EnumerateDirectories(cacheDirectory); }
            catch { return null; }

            foreach (var dir in versionDirs)
            {
                var raw = Path.GetFileName(dir);
                if (!Version.TryParse(raw, out var parsed)) continue;

                var installerPath = Path.Combine(dir, $"ReShade_Setup_{raw}_Addon.exe");
                if (!File.Exists(installerPath) || !IsValidPeFile(installerPath)) continue;

                if (best == null || parsed > best)
                {
                    best = parsed;
                    bestRaw = raw;
                }
            }

            return bestRaw == null ? null : new ReShadeVersionInfo { Version = bestRaw, Url = null };
        }

        private async Task<string> DownloadInstallerAsync(ReShadeVersionInfo versionInfo)
        {
            var versionDir = Path.Combine(cacheDirectory, versionInfo.Version);
            Directory.CreateDirectory(versionDir);
            var installerPath = Path.Combine(versionDir, $"ReShade_Setup_{versionInfo.Version}_Addon.exe");

            if (File.Exists(installerPath) && IsValidPeFile(installerPath))
                return installerPath;

            try
            {
                // reshade.me rejects the download request without a Referer header.
                var request = new HttpRequestMessage(HttpMethod.Get, versionInfo.Url);
                request.Headers.Referrer = new Uri("https://reshade.me/");

                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    File.WriteAllBytes(installerPath, bytes);
                }

                if (!IsValidPeFile(installerPath))
                {
                    Log.Error($"Downloaded file for ReShade {versionInfo.Version} isn't a valid executable (site may have returned an error page).");
                    File.Delete(installerPath);
                    return null;
                }

                return installerPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download ReShade installer for version {versionInfo.Version}.");
                return null;
            }
        }

        private static bool IsValidPeFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var header = new byte[2];
                    return stream.Read(header, 0, 2) == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
                }
            }
            catch
            {
                return false;
            }
        }

        /* An existing ReShade DLL (an earlier install of ours, or a manual one) is simply
           overwritten — backing it up as ".original" would make Uninstall restore ReShade
           instead of removing it, the same trap FileBackup.BackupIfNeeded's own doc comment
           describes for OptiScaler (and seen live on Atomic Heart as a ReShade64.dll.original
           byte-identical to ReShade64.dll). */
        private static void BackupIfNeeded(string targetPath)
        {
            if (!File.Exists(targetPath)) return;
            if (IsReShadeBinary(targetPath)) return;

            var backupPath = targetPath + ".original";
            if (File.Exists(backupPath)) return;

            try
            {
                File.Move(targetPath, backupPath);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to back up existing file at '{targetPath}' before install.");
            }
        }

        private static ReShadeInstallResult Fail(string error)
        {
            Log.Error(error);
            return new ReShadeInstallResult { Success = false, Error = error };
        }
    }
}
