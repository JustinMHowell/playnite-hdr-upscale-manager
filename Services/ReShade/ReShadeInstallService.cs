using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace HdrUpscaleManager.Services.ReShade
{
    public class ReShadeInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledDllPath { get; set; }
        public GraphicsApi Api { get; set; }
        public string Version { get; set; }
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

        /* Always installs whatever version reshade.me currently serves — there's no version
           picker yet, this will become configurable later. */
        public async Task<ReShadeInstallResult> InstallAsync(string gameInstallPath)
        {
            var exePath = PeInspector.FindGameExecutable(gameInstallPath);
            if (exePath == null)
                return Fail("No executable found in the install folder.");

            var info = PeInspector.Inspect(exePath);
            if (info == null)
                return Fail("Could not read the game executable's PE header.");

            var proxyDllName = ReShadeProxyDll.GetProxyDllName(info.Api);
            if (proxyDllName == null)
                return Fail($"{info.Api} isn't supported by a drop-in ReShade install.");

            var versionInfo = await ReShadeVersionChecker.CheckLatestAsync().ConfigureAwait(false);
            if (versionInfo == null)
                return Fail("Could not determine the latest ReShade version.");

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

            var targetPath = Path.Combine(gameInstallPath, proxyDllName);
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

            Log.Info($"Installed ReShade {versionInfo.Version} ({info.Api}, {(info.Is64Bit ? "64-bit" : "32-bit")}) to '{targetPath}'.");
            return new ReShadeInstallResult
            {
                Success = true,
                InstalledDllPath = targetPath,
                Api = info.Api,
                Version = versionInfo.Version
            };
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

        private static void BackupIfNeeded(string targetPath)
        {
            if (!File.Exists(targetPath)) return;

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
