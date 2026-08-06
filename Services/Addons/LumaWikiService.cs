using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HdrUpscaleManager.Models.Addons;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace HdrUpscaleManager.Services.Addons
{
    /* Fetches and parses Luma-Framework's wiki mod list, the source of per-game Luma downloads.
       Single page (not per-game subpages like RenoDX's wiki), table columns found by header text
       rather than fixed position. Falls back to the last successfully parsed copy on disk when
       the wiki can't be reached. Scoped down from RHI's LumaService: the "Features" column's
       long-form prose (anchor-followed sections) isn't ported, only "Special Notes". */
    public class LumaWikiService
    {
        private const string WikiUrl = "https://github.com/Filoppi/Luma-Framework/wiki";

        private static readonly HttpClient Http = new HttpClient();

        private readonly string cacheFilePath;

        public LumaWikiService(string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            cacheFilePath = Path.Combine(cacheDirectory, "luma_wiki_mods.json");
        }

        public async Task<List<LumaMod>> FetchAllAsync()
        {
            try
            {
                var html = await Http.GetStringAsync(WikiUrl).ConfigureAwait(false);
                var mods = ParseMods(html);
                if (mods.Count > 0)
                {
                    Cache(mods);
                    return mods;
                }

                Log.Warn("Luma wiki page returned no parseable mod table.");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to fetch Luma wiki mod list.");
            }

            return LoadCached();
        }

        public static List<LumaMod> ParseMods(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var mods = new List<LumaMod>();
            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null) return mods;

            foreach (var table in tables)
            {
                var headerCells = table.SelectSingleNode(".//tr")?.SelectNodes("th|td");
                if (headerCells == null || headerCells.Count < 2) continue;

                var headerTexts = headerCells.Select(h => Clean(h.InnerText).ToLowerInvariant()).ToList();

                /* The mod table's first column is "Name" and it carries a download link/column —
                   anything else on the wiki page (table of contents, notices) is skipped. */
                if (!headerTexts[0].Contains("name")) continue;
                if (!headerTexts.Any(h => h.Contains("download") || h.Contains("link"))) continue;

                mods.AddRange(ParseModTable(table, headerTexts));
            }

            return mods;
        }

        private static List<LumaMod> ParseModTable(HtmlNode table, List<string> headerTexts)
        {
            var mods = new List<LumaMod>();
            var rows = table.SelectNodes(".//tr")?.Skip(1);
            if (rows == null) return mods;

            const int nameCol = 0;
            var authorCol = -1;
            var linksCol = -1;
            var statusCol = -1;
            var notesCol = -1;

            for (var i = 0; i < headerTexts.Count; i++)
            {
                var h = headerTexts[i];
                if (h.Contains("author")) authorCol = i;
                else if (h.Contains("download") || h.Contains("link")) linksCol = i;
                else if (h.Contains("status")) statusCol = i;
                else if (h.Contains("note")) notesCol = i;
            }

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count < 2) continue;

                var name = Clean(cells[nameCol].InnerText);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var author = authorCol >= 0 && authorCol < cells.Count ? Clean(cells[authorCol].InnerText) : "";

                string downloadUrl = null;
                if (linksCol >= 0 && linksCol < cells.Count)
                {
                    var link = cells[linksCol].SelectNodes(".//a")?
                        .FirstOrDefault(a => a.GetAttributeValue("href", "").StartsWith("http", StringComparison.OrdinalIgnoreCase));
                    downloadUrl = link?.GetAttributeValue("href", null);
                }

                var status = "✅";
                if (statusCol >= 0 && statusCol < cells.Count && cells[statusCol].InnerText.Contains("\U0001F6A7"))
                    status = "🚧";

                var notes = notesCol >= 0 && notesCol < cells.Count ? BuildNoteText(cells[notesCol]) : null;

                mods.Add(new LumaMod
                {
                    Name = name,
                    Author = author,
                    DownloadUrl = downloadUrl,
                    Status = status,
                    SpecialNotes = string.IsNullOrEmpty(notes) ? null : notes
                });
            }

            return mods;
        }

        private static string BuildNoteText(HtmlNode cell)
        {
            var sb = new StringBuilder();
            foreach (var node in cell.ChildNodes)
            {
                switch (node.NodeType)
                {
                    case HtmlNodeType.Text:
                        sb.Append(Regex.Replace(HtmlEntity.DeEntitize(node.InnerText), "[ \t]+", " "));
                        break;
                    case HtmlNodeType.Element when node.Name == "br":
                        sb.Append('\n');
                        break;
                    case HtmlNodeType.Element when node.Name == "li":
                        var li = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(li)) sb.Append("\n• ").Append(li);
                        break;
                    case HtmlNodeType.Element:
                        var et = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(et)) sb.Append(et);
                        break;
                }
            }

            return Regex.Replace(sb.ToString(), "\n{3,}", "\n\n").Trim();
        }

        private static string Clean(string s) => HtmlEntity.DeEntitize(s ?? "").Trim();

        private void Cache(List<LumaMod> mods)
        {
            try
            {
                File.WriteAllText(cacheFilePath, JsonConvert.SerializeObject(mods));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to write Luma wiki mod cache to disk.");
            }
        }

        private List<LumaMod> LoadCached()
        {
            if (!File.Exists(cacheFilePath)) return new List<LumaMod>();

            try
            {
                return JsonConvert.DeserializeObject<List<LumaMod>>(File.ReadAllText(cacheFilePath))
                    ?? new List<LumaMod>();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read Luma wiki mod cache.");
                return new List<LumaMod>();
            }
        }
    }
}
