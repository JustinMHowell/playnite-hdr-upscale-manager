using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HdrUpscaleManager.Services.ReShade;

namespace HdrUpscaleManager.Services.Addons
{
    public class AddonInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledPath { get; set; }
        public string FileHash { get; set; }
        public long? RemoteContentLength { get; set; }
        public DateTimeOffset? RemoteLastModified { get; set; }
    }

    public class AddonProbeResult
    {
        public long? ContentLength { get; set; }
        public DateTimeOffset? LastModified { get; set; }
    }

    /* Shared addon-file installer — downloads a .addon64/.addon32 and drops it into the game
       folder next to the ReShade DLL. Used by RenoDX today, Luma later. Unlike
       ReShadeInstallService, there's no pre-existing game file to back up — addon files are
       never part of the original game. */
    public class AddonInstallService
    {
        private static readonly HttpClient Http = new HttpClient();

        public async Task<AddonInstallResult> InstallAsync(string gameInstallPath, string downloadUrl, string addonFileName)
        {
            byte[] bytes;
            long? contentLength;
            DateTimeOffset? lastModified;
            try
            {
                using (var response = await Http.GetAsync(downloadUrl).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    contentLength = response.Content.Headers.ContentLength;
                    lastModified = response.Content.Headers.LastModified;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download addon from '{downloadUrl}'.");
                return Fail($"Failed to download: {ex.Message}");
            }

            /* Deploy next to the real exe (and next to ReShade, which resolves the same way) —
               not gameInstallPath itself. See PeInspector.ResolveDeployDirectory's own doc
               comment for why. */
            var deployDir = PeInspector.ResolveDeployDirectory(gameInstallPath);
            var targetPath = Path.Combine(deployDir, addonFileName);
            try
            {
                File.WriteAllBytes(targetPath, bytes);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to write addon to '{targetPath}'.");
                return Fail($"Failed to write file: {ex.Message}");
            }

            Log.Info($"Installed addon '{addonFileName}' to '{targetPath}'.");
            return new AddonInstallResult
            {
                Success = true,
                InstalledPath = targetPath,
                FileHash = ComputeSha256(bytes),
                RemoteContentLength = contentLength,
                RemoteLastModified = lastModified
            };
        }

        /* HEAD request against a mod's current download URL — used to check for an update
           without re-downloading the file first. */
        public static async Task<AddonProbeResult> ProbeAsync(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return new AddonProbeResult
                    {
                        ContentLength = response.Content.Headers.ContentLength,
                        LastModified = response.Content.Headers.LastModified
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to probe addon URL '{url}'.");
                return new AddonProbeResult();
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static AddonInstallResult Fail(string error) => new AddonInstallResult { Success = false, Error = error };
    }
}
