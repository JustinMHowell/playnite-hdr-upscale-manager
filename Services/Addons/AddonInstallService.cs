using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace HdrUpscaleManager.Services.Addons
{
    public class AddonInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledPath { get; set; }
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
            try
            {
                bytes = await Http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download addon from '{downloadUrl}'.");
                return Fail($"Failed to download: {ex.Message}");
            }

            var targetPath = Path.Combine(gameInstallPath, addonFileName);
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
            return new AddonInstallResult { Success = true, InstalledPath = targetPath };
        }

        private static AddonInstallResult Fail(string error) => new AddonInstallResult { Success = false, Error = error };
    }
}
