using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HdrUpscaleManager.Services.ReShade;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.Services.Addons
{
    public class PcGamingWikiLookupResult
    {
        public string PageTitle { get; set; }
        /* Raw "Engines" text from PCGamingWiki's Infobox_game table (e.g. "Engine:LS3D") — kept
           even when it doesn't map to a RenoDX-supported engine, so the UI can still show it. */
        public string EngineRaw { get; set; }
        public RenoDxEngine? MappedEngine { get; set; }
        public List<string> Direct3DVersionsRaw { get; set; }
        /* Only set when every listed Direct3D version (across all patches/re-releases) falls
           within DXVK+Lilium's supported range — see InferConfidentGraphicsApi. A game that ever
           shipped an 11/12 path is left null: on an uninstalled game there's no way to tell which
           path its executable actually loads, and a false "yes" would be worse than no answer. */
        public GraphicsApi? ConfidentGraphicsApi { get; set; }
    }

    /* Fallback engine/graphics-API lookup via PCGamingWiki's public Cargo query API, used only
       when local detection (RenoDxEngineDetector's folder scan, PeInspector's PE-header import
       scan) can't tell — typically because the game isn't installed yet. Not addon-dependent:
       the actual Playnite "PCGamingWiki Metadata Provider" addon doesn't import this data (its
       source only maps controller/multiplayer/VR features), but PCGamingWiki's own `API` and
       `Infobox_game` Cargo tables carry it directly and are free to query without any addon.
       Cached to disk per normalized game name — the same one-file-per-store convention as
       JsonRecordStore's install records, just keyed by name instead of Playnite game Id since
       there's no install record to key off yet. */
    public class PcGamingWikiService
    {
        private const string ApiUrl = "https://www.pcgamingwiki.com/w/api.php";
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Regex LeadingVersionNumber = new Regex(@"^(\d+)");

        private readonly JsonRecordStore<PcGamingWikiLookupResult> cache;

        public PcGamingWikiService(string cacheDirectory)
        {
            cache = new JsonRecordStore<PcGamingWikiLookupResult>(cacheDirectory, "pcgamingwiki_lookup_cache.json");
        }

        public async Task<PcGamingWikiLookupResult> LookupAsync(string gameName)
        {
            var cacheKey = GameNameMatcher.NormalizeName(gameName);
            var cached = cache.Get(cacheKey);
            if (cached != null) return cached;

            var result = new PcGamingWikiLookupResult();
            try
            {
                var pageTitle = await ResolvePageTitleAsync(gameName).ConfigureAwait(false);
                if (pageTitle != null)
                {
                    result.PageTitle = pageTitle;
                    result.Direct3DVersionsRaw = await QueryDirect3DVersionsAsync(pageTitle).ConfigureAwait(false);
                    result.ConfidentGraphicsApi = InferConfidentGraphicsApi(result.Direct3DVersionsRaw);

                    result.EngineRaw = CleanEngineName(await QueryEnginesAsync(pageTitle).ConfigureAwait(false));
                    result.MappedEngine = MapEngine(result.EngineRaw);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"PCGamingWiki lookup failed for '{gameName}'.");
            }

            cache.Set(cacheKey, result);
            return result;
        }

        private static async Task<string> ResolvePageTitleAsync(string gameName)
        {
            var url = $"{ApiUrl}?action=opensearch&search={Uri.EscapeDataString(gameName)}&limit=1&namespace=0&format=json";
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            var titles = JArray.Parse(json).ElementAtOrDefault(1) as JArray;
            return titles != null && titles.Count > 0 ? (string)titles[0] : null;
        }

        private static async Task<List<string>> QueryDirect3DVersionsAsync(string pageTitle)
        {
            var url = $"{ApiUrl}?action=cargoquery&tables=API" +
                "&fields=API.Direct3D_versions=D3D" +
                $"&where=API._pageName=\"{EscapeCargoValue(pageTitle)}\"&format=json";
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            var raw = (string)FirstRow(json)?["D3D"];
            return ParseCargoList(raw);
        }

        private static async Task<string> QueryEnginesAsync(string pageTitle)
        {
            var url = $"{ApiUrl}?action=cargoquery&tables=Infobox_game" +
                "&fields=Infobox_game.Engines=Engines" +
                $"&where=Infobox_game._pageName=\"{EscapeCargoValue(pageTitle)}\"&format=json";
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            return (string)FirstRow(json)?["Engines"];
        }

        private static JToken FirstRow(string cargoQueryJson) =>
            (JObject.Parse(cargoQueryJson)["cargoquery"] as JArray)?.FirstOrDefault()?["title"];

        public static List<string> ParseCargoList(string raw) =>
            string.IsNullOrWhiteSpace(raw)
                ? new List<string>()
                : raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /* PCGamingWiki's "Engines" field is a wikitext-link string like "Engine:LS3D" or
           "Engine:Unreal Engine 4, Engine:Havok" — strip the namespace prefix, keep the first
           listed engine (good enough for the "generic Unreal/Unity install" gating this feeds).
           Public/static so tests can feed it raw Cargo field values without a network call. */
        public static string CleanEngineName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var first = raw.Split(',')[0].Trim();
            var colon = first.IndexOf(':');
            return colon >= 0 ? first.Substring(colon + 1).Trim() : first;
        }

        public static RenoDxEngine? MapEngine(string engineName)
        {
            if (string.IsNullOrEmpty(engineName)) return null;
            if (engineName.IndexOf("unreal", StringComparison.OrdinalIgnoreCase) >= 0) return RenoDxEngine.UnrealEngine;
            if (engineName.IndexOf("unity", StringComparison.OrdinalIgnoreCase) >= 0) return RenoDxEngine.UnityEngine;
            return null;
        }

        /* Every version PCGamingWiki lists for the game (across all releases/patches) must fall
           within 8/9/10 for this to return non-null — see ConfidentGraphicsApi's doc comment. */
        public static GraphicsApi? InferConfidentGraphicsApi(List<string> versions)
        {
            if (versions == null || versions.Count == 0) return null;

            GraphicsApi? best = null;
            foreach (var version in versions)
            {
                var match = LeadingVersionNumber.Match(version);
                if (!match.Success) return null;

                switch (match.Groups[1].Value)
                {
                    case "8": best = Max(best, GraphicsApi.D3D8); break;
                    case "9": best = Max(best, GraphicsApi.D3D9); break;
                    case "10": best = Max(best, GraphicsApi.D3D10); break;
                    default: return null; // 11, 12, or anything unrecognized — not confident.
                }
            }

            return best;
        }

        private static GraphicsApi Max(GraphicsApi? current, GraphicsApi candidate) =>
            current == null || candidate > current.Value ? candidate : current.Value;

        private static string EscapeCargoValue(string value) => value.Replace("\"", "\\\"");

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "playnite-hdr-upscale-manager (PCGamingWiki Cargo API client)");
            return client;
        }
    }
}
