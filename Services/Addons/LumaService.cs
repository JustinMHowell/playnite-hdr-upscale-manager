using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models.Addons;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class LumaMatchedInstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string InstalledPath { get; set; }
        public string MatchedGameName { get; set; }
        public string Notes { get; set; }
        public List<string> InstalledFiles { get; set; }
        public string DownloadUrl { get; set; }
    }

    public enum LumaUpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        /* Locally-installed builds have no known build number to compare against. */
        UnknownLocalInstall,
        CouldNotCheck
    }

    public class LumaUpdateCheckResult
    {
        public LumaUpdateStatus Status { get; set; }
        public int LatestBuildNumber { get; set; }
    }

    public class LumaUninstallResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /* Luma-Framework install, matched via LumaWikiService/GameNameMatcher. Scoped down from
       RHI's LumaService: no reshade.ini AddonPath merging (installs to game root, same as this
       plugin's ReShade/RenoDX installs), no bundled shader-pack sync, no drag-drop/local-archive
       install. Zip payloads are extracted with System.IO.Compression rather than shelling out to
       7z, since Luma ships plain zips (unlike ReShade's PE-appended archive). */
    public static class LumaService
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/Filoppi/Luma-Framework/releases/latest";

        private static readonly HttpClient Http = CreateHttpClient();

        public static async Task<LumaMatchedInstallResult> InstallForGameAsync(
            string gameInstallPath,
            string gameName,
            string wikiCacheDirectory,
            IDictionary<string, string> nameOverrides = null)
        {
            var wikiService = new LumaWikiService(wikiCacheDirectory);
            var mods = await wikiService.FetchAllAsync().ConfigureAwait(false);
            var match = GameNameMatcher.Match(gameName, mods, m => m.Name, nameOverrides);
            if (match == null)
                return new LumaMatchedInstallResult { Success = false, Error = $"No Luma mod found for '{gameName}' on the wiki." };

            if (string.IsNullOrEmpty(match.DownloadUrl))
                return Fail($"'{match.Name}' is listed on the Luma wiki but has no download link yet.", match.Name);

            /* Security gate ported from RHI: only trust downloads hosted under the Luma-Framework
               project's own GitHub account. */
            if (!match.DownloadUrl.StartsWith("https://github.com/Filoppi/", StringComparison.OrdinalIgnoreCase))
                return Fail($"Refusing to install '{match.Name}' — its download link isn't hosted under github.com/Filoppi ({match.DownloadUrl}).", match.Name);

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(match.DownloadUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to download Luma mod from '{match.DownloadUrl}'.");
                return Fail($"Failed to download: {ex.Message}", match.Name);
            }

            List<string> installedFiles;
            try
            {
                installedFiles = Deploy(gameInstallPath, match.DownloadUrl, bytes);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deploy Luma mod for '{match.Name}'.");
                return Fail($"Failed to install: {ex.Message}", match.Name);
            }

            Log.Info($"Installed Luma mod '{match.Name}' to '{gameInstallPath}' ({installedFiles.Count} file(s)).");
            return new LumaMatchedInstallResult
            {
                Success = true,
                InstalledPath = gameInstallPath,
                MatchedGameName = match.Name,
                Notes = match.SpecialNotes,
                InstalledFiles = installedFiles,
                DownloadUrl = match.DownloadUrl
            };
        }

        private static List<string> Deploy(string gameInstallPath, string downloadUrl, byte[] bytes)
        {
            var installedFiles = new List<string>();
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var stream = new MemoryStream(bytes))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        /* A directory entry has an empty Name (its FullName ends in '/'). */
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        /* RHI deploys its own reshade.ini — never let the mod archive overwrite it. */
                        if (string.Equals(entry.Name, "reshade.ini", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var destPath = Path.Combine(gameInstallPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? gameInstallPath);
                        entry.ExtractToFile(destPath, overwrite: true);
                        installedFiles.Add(entry.FullName);
                    }
                }
            }
            else
            {
                /* Bare-DLL distribution — RHI names it dxgi.dll unless the source already is. */
                var targetName = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? fileName : "dxgi.dll";
                var targetPath = Path.Combine(gameInstallPath, targetName);
                File.WriteAllBytes(targetPath, bytes);
                installedFiles.Add(targetName);
            }

            return installedFiles;
        }

        public static async Task<LumaUpdateCheckResult> CheckForUpdateAsync(LumaInstalledRecord record)
        {
            if (record.InstalledBuildNumber <= 0)
                return new LumaUpdateCheckResult { Status = LumaUpdateStatus.UnknownLocalInstall };

            var latest = await GetLatestBuildNumberAsync().ConfigureAwait(false);
            if (latest == null)
                return new LumaUpdateCheckResult { Status = LumaUpdateStatus.CouldNotCheck };

            return new LumaUpdateCheckResult
            {
                Status = latest.Value > record.InstalledBuildNumber ? LumaUpdateStatus.UpdateAvailable : LumaUpdateStatus.UpToDate,
                LatestBuildNumber = latest.Value
            };
        }

        public static LumaUninstallResult Uninstall(LumaInstalledRecord record)
        {
            var installRoot = Path.GetFullPath(record.InstallPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var failures = new List<string>();

            /* A zip-based install can nest files under subfolders it creates itself (e.g.
               "Luma/INSIDE/Includes/..."), unlike RenoDX's single addon file straight in the
               game root. Deleting only the tracked files left those now-empty subfolders behind
               — confirmed live 2026-08-08 (a real Luma install/uninstall left an empty
               Luma\Global, Luma\Includes, Luma\INSIDE\Includes tree in the game's own folder).
               Collecting each file's ancestor directories up to (but excluding) the game's
               install root, then removing them deepest-first only if actually empty, cleans
               that up without ever touching a directory the game itself owns. */
            var candidateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var relativePath in record.InstalledFiles ?? new List<string>())
            {
                try
                {
                    var fullPath = Path.Combine(record.InstallPath, relativePath);
                    if (File.Exists(fullPath)) File.Delete(fullPath);

                    var dir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
                    while (!string.IsNullOrEmpty(dir) &&
                           !string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), installRoot, StringComparison.OrdinalIgnoreCase) &&
                           dir.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        candidateDirectories.Add(dir);
                        dir = Path.GetDirectoryName(dir);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, $"Failed to delete Luma file '{relativePath}' for '{record.GameName}'.");
                    failures.Add(relativePath);
                }
            }

            foreach (var dir in candidateDirectories.OrderByDescending(d => d.Length))
            {
                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                        Directory.Delete(dir);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, $"Failed to remove now-empty Luma directory '{dir}' for '{record.GameName}'.");
                }
            }

            if (failures.Count > 0)
                return new LumaUninstallResult { Success = false, Error = $"Could not delete: {string.Join(", ", failures)}" };

            Log.Info($"Uninstalled Luma from '{record.GameName}'.");
            return new LumaUninstallResult { Success = true };
        }

        public static async Task<int?> GetLatestBuildNumberAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(json);
                var tag = (string)parsed["tag_name"];
                if (string.IsNullOrEmpty(tag)) return null;

                var lastDash = tag.LastIndexOf('-');
                if (lastDash < 0 || lastDash == tag.Length - 1) return null;

                return int.TryParse(tag.Substring(lastDash + 1), out var buildNumber) ? buildNumber : (int?)null;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to check the latest Luma Framework release.");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }

        private static LumaMatchedInstallResult Fail(string error, string matchedName) =>
            new LumaMatchedInstallResult { Success = false, Error = error, MatchedGameName = matchedName };
    }
}
