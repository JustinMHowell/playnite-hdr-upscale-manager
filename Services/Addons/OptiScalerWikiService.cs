using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace HdrUpscaleManager.Services.Addons
{
    /* Fetches and parses OptiScaler's own GitHub wiki Compatibility List and FSR4 Compatibility
       List pages — the "Upscaler Inputs" column answers "does this game already ship FSR/XeSS,
       or DLSS only", which the manager window shows next to the two OptiScaler Install buttons
       so the user doesn't have to guess or dig through the game's own graphics settings.
       Parsing logic ported from RHI's OptiScalerWikiService (see THIRD_PARTY_NOTICES.md) — same
       source, same table shape. Purely informational: this addon still leaves the actual Install
       button choice to the user, same as RHI's own combo box does, per this project's "no silent
       choices" principle. */
    public class OptiScalerWikiService
    {
        private const string CompatibilityListUrl = "https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List";
        private const string Fsr4CompatibilityListUrl = "https://github.com/optiscaler/OptiScaler/wiki/FSR4-Compatibility-List";
        private const string WikiBaseUrl = "https://github.com/optiscaler/OptiScaler/wiki/";

        private static readonly HttpClient Http = new HttpClient();
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

        private readonly string cacheFilePath;

        public OptiScalerWikiService(string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            cacheFilePath = Path.Combine(cacheDirectory, "optiscaler_wiki_cache.json");
        }

        /* Synchronous, disk-only read of whatever the last successful FetchAsync cached — no
           network call, safe to use before a background fetch has had a chance to run. */
        public OptiScalerWikiData TryGetCachedData()
        {
            if (!File.Exists(cacheFilePath)) return null;

            try
            {
                var data = JsonConvert.DeserializeObject<OptiScalerWikiData>(File.ReadAllText(cacheFilePath));
                OptiScalerWikiData.Normalize(data);
                return data;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read OptiScaler wiki cache.");
                return null;
            }
        }

        public async Task<OptiScalerWikiData> FetchAsync()
        {
            var cached = TryGetCachedData();
            if (cached != null && DateTime.UtcNow - cached.FetchedAtUtc < CacheLifetime)
                return cached;

            var standardHtml = await FetchPageSafeAsync(CompatibilityListUrl).ConfigureAwait(false);
            var fsr4Html = await FetchPageSafeAsync(Fsr4CompatibilityListUrl).ConfigureAwait(false);

            if (standardHtml == null && fsr4Html == null)
                return cached; // Network failed — an older cache is still better than nothing.

            var data = new OptiScalerWikiData { FetchedAtUtc = DateTime.UtcNow };
            if (standardHtml != null) ParseCompatibilityPage(standardHtml, data.StandardCompat);
            if (fsr4Html != null) ParseCompatibilityPage(fsr4Html, data.Fsr4Compat);

            Cache(data);
            return data;
        }

        private static async Task<string> FetchPageSafeAsync(string url)
        {
            try
            {
                return await Http.GetStringAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to fetch OptiScaler wiki page '{url}'.");
                return null;
            }
        }

        private void Cache(OptiScalerWikiData data)
        {
            try
            {
                File.WriteAllText(cacheFilePath, JsonConvert.SerializeObject(data));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to write OptiScaler wiki cache to disk.");
            }
        }

        internal static void ParseCompatibilityPage(string html, Dictionary<string, OptiScalerCompatEntry> entries)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null) return;

            foreach (var table in tables)
            {
                ParseTable(table, entries);
            }
        }

        private class ColumnIndexes
        {
            public int Upscalers = 3;
            public int Notes = 4;
        }

        internal static void ParseTable(HtmlNode table, Dictionary<string, OptiScalerCompatEntry> entries)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 2) return;

            var headerRow = rows.FirstOrDefault(r => (r.SelectNodes("th")?.Count ?? 0) > 0);
            var columns = ResolveColumnIndexes(headerRow);

            var dataRows = rows.Where(r => (r.SelectNodes("th")?.Count ?? 0) == 0);

            foreach (var row in dataRows)
            {
                try
                {
                    ParseRow(row, columns, entries);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Failed to parse an OptiScaler wiki compatibility table row.");
                }
            }
        }

        /* Locates the "Upscalers"/"Upscaler Inputs" and "Notes" columns by their header text
           instead of a fixed index — confirmed live 2026-08-09 that the wiki's actual column
           order (Game, Compatibility, Upscaler Inputs, OptiPatcher Support, Notes, Screenshots)
           doesn't match a hardcoded index 3 (that landed on the OptiPatcher-support marker "✨"
           instead of the real upscaler list for Deep Rock Galactic). Falls back to indexes 3/4
           if no header row is found at all. */
        private static ColumnIndexes ResolveColumnIndexes(HtmlNode headerRow)
        {
            var result = new ColumnIndexes();
            var headers = headerRow?.SelectNodes("th");
            if (headers == null) return result;

            for (var i = 0; i < headers.Count; i++)
            {
                var text = Clean(headers[i].InnerText);
                if (text.IndexOf("upscaler", StringComparison.OrdinalIgnoreCase) >= 0) result.Upscalers = i;
                else if (text.IndexOf("note", StringComparison.OrdinalIgnoreCase) >= 0) result.Notes = i;
            }

            return result;
        }

        private static void ParseRow(HtmlNode row, ColumnIndexes columns, Dictionary<string, OptiScalerCompatEntry> entries)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 2) return;

            var gameName = Clean(cells[0].InnerText);
            if (string.IsNullOrWhiteSpace(gameName)) return;

            var nameLink = cells[0].SelectSingleNode(".//a");
            var detailPageUrl = nameLink != null ? ResolveUrl(nameLink.GetAttributeValue("href", "").Trim()) : null;

            var statusText = cells.Count > 1 ? cells[1].InnerText : "";
            var status = statusText.Contains("✔") ? "Working" : statusText.Contains("❌") ? "Not Working" : "Unknown";

            var upscalers = cells.Count > columns.Upscalers
                ? Clean(cells[columns.Upscalers].InnerText).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
                : new List<string>();

            var notes = cells.Count > columns.Notes ? Clean(cells[columns.Notes].InnerText) : null;
            if (string.IsNullOrWhiteSpace(notes)) notes = null;

            // First occurrence wins — one row per game on the real wiki tables.
            if (!entries.ContainsKey(gameName))
            {
                entries[gameName] = new OptiScalerCompatEntry
                {
                    GameName = gameName,
                    Status = status,
                    Upscalers = upscalers,
                    Notes = notes,
                    DetailPageUrl = detailPageUrl
                };
            }
        }

        private static string ResolveUrl(string href)
        {
            if (string.IsNullOrEmpty(href)) return null;
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
            return href.StartsWith("/") ? "https://github.com" + href : WikiBaseUrl + href.TrimStart('.', '/');
        }

        /* Normalizes curly/smart quotes to straight ASCII ones so wiki game names match Steam
           names, which use standard characters. */
        private static string Clean(string s) => NormalizeQuotes(HtmlEntity.DeEntitize(s ?? "").Trim());

        private static string NormalizeQuotes(string s) =>
            s.Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"');

        /* Remaps a detected game name to the OptiScaler wiki's own naming when they differ —
           same manifest field RHI itself uses for this lookup. */
        public static string ResolveWikiName(string gameName, RemoteManifest manifest)
        {
            if (manifest?.OptiScalerWikiNames != null &&
                manifest.OptiScalerWikiNames.TryGetValue(gameName, out var wikiName) &&
                !string.IsNullOrWhiteSpace(wikiName))
            {
                return wikiName;
            }

            return gameName;
        }
    }
}
