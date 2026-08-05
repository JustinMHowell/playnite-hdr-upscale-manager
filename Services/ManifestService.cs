using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services
{
    public class ManifestFetchResult
    {
        public RemoteManifest Manifest { get; set; }
        /* "GitHubApi" | "RawFallback" | "Cache" */
        public string Source { get; set; }
        /* manifest.json blob sha, when known (GitHubApi only) */
        public string Sha { get; set; }
        public DateTime FetchedAtUtc { get; set; }
    }

    /* Fetch chain observed in RHI's ManifestService: GitHub API, then raw.githubusercontent.com,
       then local disk cache. URLs point at RHI's own repo on purpose, not user-configurable. */
    public class ManifestService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/RankFTW/RHI/contents/manifest.json";
        private const string RawFallbackUrl = "https://raw.githubusercontent.com/RankFTW/RHI/main/manifest.json";

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly string cacheFilePath;
        private readonly string syncMetaFilePath;

        public ManifestService(string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            cacheFilePath = Path.Combine(cacheDirectory, "manifest.json");
            syncMetaFilePath = Path.Combine(cacheDirectory, "manifest.sync_meta.json");
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            // GitHub's API rejects requests with no User-Agent header.
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager");
            return client;
        }

        public async Task<ManifestFetchResult> FetchAsync()
        {
            var viaApi = await TryFetchViaGitHubApiAsync().ConfigureAwait(false);
            if (viaApi != null)
            {
                Cache(viaApi.Item1, viaApi.Item2);
                return Deserialize(viaApi.Item1, "GitHubApi", viaApi.Item2);
            }

            var viaRaw = await TryFetchRawAsync().ConfigureAwait(false);
            if (viaRaw != null)
            {
                Cache(viaRaw, sha: null);
                return Deserialize(viaRaw, "RawFallback", sha: null);
            }

            return LoadCached();
        }

        private async Task<Tuple<string, string>> TryFetchViaGitHubApiAsync()
        {
            try
            {
                var response = await Http.GetStringAsync(GitHubApiUrl).ConfigureAwait(false);
                var parsed = JObject.Parse(response);
                var contentBase64 = ((string)parsed["content"])?.Replace("\n", "").Replace("\r", "");
                if (string.IsNullOrEmpty(contentBase64)) return null;

                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64));
                var sha = (string)parsed["sha"];
                return Tuple.Create(json, sha);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Manifest fetch via GitHub API failed.");
                return null;
            }
        }

        private async Task<string> TryFetchRawAsync()
        {
            try
            {
                return await Http.GetStringAsync(RawFallbackUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Manifest fetch via raw fallback URL failed.");
                return null;
            }
        }

        private void Cache(string json, string sha)
        {
            try
            {
                File.WriteAllText(cacheFilePath, json);
                var meta = new SyncMeta { Sha = sha, FetchedAtUtc = DateTime.UtcNow };
                File.WriteAllText(syncMetaFilePath, JsonConvert.SerializeObject(meta));
            }
            catch (Exception ex)
            {
                // A cache-write failure shouldn't fail the fetch itself, just get logged.
                Log.Warn(ex, "Failed to write manifest cache to disk.");
            }
        }

        private ManifestFetchResult LoadCached()
        {
            if (!File.Exists(cacheFilePath)) return null;

            var json = File.ReadAllText(cacheFilePath);
            var result = Deserialize(json, "Cache", sha: null);
            if (result == null) return null;

            if (File.Exists(syncMetaFilePath))
            {
                try
                {
                    var meta = JsonConvert.DeserializeObject<SyncMeta>(File.ReadAllText(syncMetaFilePath));
                    result.Sha = meta?.Sha;
                    result.FetchedAtUtc = meta?.FetchedAtUtc ?? result.FetchedAtUtc;
                }
                catch (Exception ex)
                {
                    // Corrupt sidecar — the manifest itself still loaded fine, just no metadata.
                    Log.Warn(ex, "Failed to read manifest sync metadata.");
                }
            }

            return result;
        }

        private static ManifestFetchResult Deserialize(string json, string source, string sha)
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);
                if (manifest == null) return null;

                RemoteManifest.Normalize(manifest);
                return new ManifestFetchResult
                {
                    Manifest = manifest,
                    Source = source,
                    Sha = sha,
                    FetchedAtUtc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize manifest JSON (source: {source}).");
                return null;
            }
        }

        private class SyncMeta
        {
            public string Sha { get; set; }
            public DateTime FetchedAtUtc { get; set; }
        }
    }
}
