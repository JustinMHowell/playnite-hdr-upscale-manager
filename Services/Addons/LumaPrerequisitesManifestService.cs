using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models.Addons;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class LumaPrerequisitesManifestFetchResult
    {
        public LumaPrerequisitesManifest Manifest { get; set; }
        /* "GitHubApi" | "RawFallback" | "Cache" | "Bundled" */
        public string Source { get; set; }
        public DateTime FetchedAtUtc { get; set; }
    }

    /* Our OWN self-maintained manifest of Luma install prerequisites (e.g. dgVoodoo2 for
       pre-DX11 games) — deliberately separate from ManifestService/RemoteManifest, which fetches
       community data from RankFTW/RHI's own repo. Same 3-tier fetch chain as ManifestService
       (GitHub Contents API -> raw fallback -> local disk cache), pointed at THIS repo instead,
       plus one tier below that: a bundled copy (Resources/luma_prerequisites.json) so a first-
       ever run still has real data before any network fetch has succeeded — the committed
       root-level luma_prerequisites.json is the source of truth kept in sync with it. */
    public class LumaPrerequisitesManifestService
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/matheusfaustino/playnite-hdr-upscale-manager/contents/luma_prerequisites.json";
        private const string RawFallbackUrl =
            "https://raw.githubusercontent.com/matheusfaustino/playnite-hdr-upscale-manager/main/luma_prerequisites.json";
        private const string BundledResourceName = "HdrUpscaleManager.Resources.luma_prerequisites.json";

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly string cacheFilePath;

        public LumaPrerequisitesManifestService(string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            cacheFilePath = Path.Combine(cacheDirectory, "luma_prerequisites.json");
        }

        /* Synchronous, disk-only read — no network call, safe to use from menu-building code
           that can't await. Falls all the way back to the bundled copy if nothing's cached yet. */
        public LumaPrerequisitesManifest TryGetCachedManifest() =>
            LoadCached()?.Manifest ?? LoadBundled()?.Manifest;

        public async Task<LumaPrerequisitesManifestFetchResult> FetchAsync()
        {
            var viaApi = await TryFetchViaGitHubApiAsync().ConfigureAwait(false);
            if (viaApi != null)
            {
                Cache(viaApi);
                return Deserialize(viaApi, "GitHubApi");
            }

            var viaRaw = await TryFetchRawAsync().ConfigureAwait(false);
            if (viaRaw != null)
            {
                Cache(viaRaw);
                return Deserialize(viaRaw, "RawFallback");
            }

            return LoadCached() ?? LoadBundled();
        }

        private static async Task<string> TryFetchViaGitHubApiAsync()
        {
            try
            {
                var response = await Http.GetStringAsync(GitHubApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(response);
                var contentBase64 = ((string)parsed["content"])?.Replace("\n", "").Replace("\r", "");
                if (string.IsNullOrEmpty(contentBase64)) return null;

                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Luma prerequisites manifest fetch via GitHub API failed.");
                return null;
            }
        }

        private static async Task<string> TryFetchRawAsync()
        {
            try
            {
                return await Http.GetStringAsync(RawFallbackUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Luma prerequisites manifest fetch via raw fallback URL failed.");
                return null;
            }
        }

        private void Cache(string json)
        {
            try
            {
                File.WriteAllText(cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to write Luma prerequisites manifest cache to disk.");
            }
        }

        private LumaPrerequisitesManifestFetchResult LoadCached()
        {
            if (!File.Exists(cacheFilePath)) return null;
            return Deserialize(File.ReadAllText(cacheFilePath), "Cache");
        }

        private static LumaPrerequisitesManifestFetchResult LoadBundled()
        {
            try
            {
                using (var stream = typeof(LumaPrerequisitesManifestService).Assembly.GetManifestResourceStream(BundledResourceName))
                {
                    if (stream == null) return null;

                    using (var reader = new StreamReader(stream))
                    {
                        return Deserialize(reader.ReadToEnd(), "Bundled");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read the bundled Luma prerequisites manifest.");
                return null;
            }
        }

        private static LumaPrerequisitesManifestFetchResult Deserialize(string json, string source)
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<LumaPrerequisitesManifest>(json);
                if (manifest == null) return null;

                LumaPrerequisitesManifest.Normalize(manifest);
                return new LumaPrerequisitesManifestFetchResult
                {
                    Manifest = manifest,
                    Source = source,
                    FetchedAtUtc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize Luma prerequisites manifest JSON (source: {source}).");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }
    }
}
