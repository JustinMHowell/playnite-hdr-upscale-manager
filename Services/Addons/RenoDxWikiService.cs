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
    /* Fetches and parses RenoDX's wiki mod list, the source of per-game addon downloads.
       Table layout and column-detection logic observed in RHI's WikiService — the wiki uses
       more than one table format (4-col with a maintainer column, 3-col without), so columns
       are found by header text rather than fixed position. Falls back to the last successfully
       parsed copy on disk when the wiki can't be reached. */
    public class RenoDxWikiService
    {
        private const string WikiUrl = "https://github.com/clshortfuse/renodx/wiki/Mods";

        private static readonly HttpClient Http = new HttpClient();

        private readonly string cacheFilePath;

        public RenoDxWikiService(string cacheDirectory)
        {
            Directory.CreateDirectory(cacheDirectory);
            cacheFilePath = Path.Combine(cacheDirectory, "renodx_wiki_mods.json");
        }

        public async Task<List<RenoDxWikiMod>> FetchAllAsync()
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

                Log.Warn("RenoDX wiki page returned no parseable mod tables.");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to fetch RenoDX wiki mod list.");
            }

            return LoadCached();
        }

        public static List<RenoDxWikiMod> ParseMods(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var mods = new List<RenoDxWikiMod>();
            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null) return mods;

            foreach (var table in tables)
            {
                var headerCells = table.SelectSingleNode(".//tr")?.SelectNodes("th|td");
                if (headerCells == null || headerCells.Count < 2) continue;

                var headerTexts = headerCells.Select(h => Clean(h.InnerText).ToLowerInvariant()).ToList();
                var hasLinksCol = headerTexts.Any(h => h.Contains("link"));
                var hasStatusCol = headerTexts.Any(h => h.Contains("status"));

                /* Only tables that carry download links (or a status column, alongside at
                   least a name column) describe installable mods — anything else on the wiki
                   page (a notice, a table of contents) is skipped. */
                if (!hasLinksCol && !(hasStatusCol && headerCells.Count >= 3)) continue;

                mods.AddRange(ParseModTable(table, headerTexts));
            }

            return mods;
        }

        private static List<RenoDxWikiMod> ParseModTable(HtmlNode table, List<string> headerTexts)
        {
            var mods = new List<RenoDxWikiMod>();
            var rows = table.SelectNodes(".//tr")?.Skip(1);
            if (rows == null) return mods;

            const int nameCol = 0;
            var maintainerCol = -1;
            var linksCol = -1;
            var statusCol = -1;

            for (var i = 0; i < headerTexts.Count; i++)
            {
                var h = headerTexts[i];
                if (h.Contains("maintainer") || h.Contains("author") || h.Contains("developer"))
                    maintainerCol = i;
                else if (h.Contains("link") || h.Contains("download"))
                    linksCol = i;
                else if (h.Contains("status"))
                    statusCol = i;
            }

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count < 2) continue;

                var name = Clean(cells[nameCol].InnerText);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string nameUrl = null;
                var nameLink = cells[nameCol].SelectSingleNode(".//a");
                if (nameLink != null)
                {
                    var href = nameLink.GetAttributeValue("href", "").Trim();
                    if (!string.IsNullOrEmpty(href))
                    {
                        nameUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href
                            : href.StartsWith("/") ? "https://github.com" + href
                            : "https://github.com/clshortfuse/renodx/wiki/" + href.TrimStart('.', '/');
                    }
                }

                var maintainer = maintainerCol >= 0 && maintainerCol < cells.Count
                    ? Clean(cells[maintainerCol].InnerText) : "";
                var statusCellNode = statusCol >= 0 && statusCol < cells.Count ? cells[statusCol] : null;

                string snapshotUrl = null, snapshotUrl32 = null, nexusUrl = null, discordUrl = null;

                /* When there's a dedicated links column, only scan that cell for download links —
                   otherwise the wiki embeds them in varying columns, so scan every cell. */
                var cellsToScan = linksCol >= 0 ? new[] { cells[linksCol] } : cells.ToArray();
                foreach (var cell in cellsToScan)
                {
                    foreach (var a in cell.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var href = a.GetAttributeValue("href", "");
                        var text = a.InnerText.Trim().ToLowerInvariant();
                        if (href.Contains(".github.io/renodx/") || href.EndsWith(".addon64") ||
                            href.EndsWith(".addon32") || href.Contains("/releases/download/"))
                        {
                            if (href.EndsWith(".addon32"))
                                snapshotUrl32 = href;
                            else
                                snapshotUrl = href;
                        }
                        else if (href.Contains("nexusmods.com"))
                            nexusUrl = href;
                        else if (href.Contains("discord.com") || href.Contains("discord.gg"))
                            discordUrl = href;
                        else if (text.Contains("snapshot") || text.Contains("download"))
                            snapshotUrl = snapshotUrl ?? href;
                    }
                }

                var isWorkInProgress = false;
                string notes = null;
                if (statusCellNode != null)
                {
                    isWorkInProgress = statusCellNode.InnerText.Contains("\U0001F6A7");
                    var titledLink = statusCellNode.SelectNodes(".//a[@title]")?.FirstOrDefault();
                    var title = titledLink?.GetAttributeValue("title", "").Trim();
                    if (!string.IsNullOrEmpty(title)) notes = title;
                }

                if (string.IsNullOrEmpty(notes))
                {
                    for (var i = 0; i < cells.Count; i++)
                    {
                        if (i == nameCol || i == maintainerCol || i == linksCol) continue;

                        if (i == statusCol)
                        {
                            var noteAfterEmoji = Clean(cells[i].InnerText)
                                .Replace("✅", "").Replace("\U0001F6A7", "")
                                .Trim(' ', '\n', '\r', '\t');
                            if (!string.IsNullOrEmpty(noteAfterEmoji)) { notes = noteAfterEmoji; break; }
                            continue;
                        }

                        var cellNote = BuildNoteText(cells[i]);
                        if (!string.IsNullOrEmpty(cellNote?.Trim())) { notes = cellNote.Trim(); break; }
                    }
                }

                /* A 32-bit-only mod (no 64-bit build) still needs to be installable — promote it
                   to the primary URL so the caller doesn't have to special-case a null 64-bit slot. */
                if (snapshotUrl == null && snapshotUrl32 != null)
                    snapshotUrl = snapshotUrl32;

                mods.Add(new RenoDxWikiMod
                {
                    Name = name,
                    Maintainer = maintainer,
                    SnapshotUrl = snapshotUrl,
                    SnapshotUrl32 = snapshotUrl32,
                    NexusUrl = nexusUrl,
                    DiscordUrl = discordUrl,
                    IsWorkInProgress = isWorkInProgress,
                    Notes = notes,
                    NameUrl = nameUrl
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
                    case HtmlNodeType.Element when node.Name == "code" || node.Name == "kbd" || node.Name == "samp":
                        var ct = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(ct)) sb.Append($"`{ct}`");
                        break;
                    case HtmlNodeType.Element:
                        var et = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(et)) sb.Append(et);
                        break;
                }
            }

            return Regex.Replace(sb.ToString(), "\n{3,}", "\n\n").Trim();
        }

        private static string Clean(string s) => NormalizeQuotes(HtmlEntity.DeEntitize(s ?? "").Trim());

        /* Wiki game names use curly/smart quotes; Steam/Playnite names use straight ones. */
        private static string NormalizeQuotes(string s) => s
            .Replace('‘', '\'').Replace('’', '\'')
            .Replace('“', '"').Replace('”', '"');

        private void Cache(List<RenoDxWikiMod> mods)
        {
            try
            {
                File.WriteAllText(cacheFilePath, JsonConvert.SerializeObject(mods));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to write RenoDX wiki mod cache to disk.");
            }
        }

        private List<RenoDxWikiMod> LoadCached()
        {
            if (!File.Exists(cacheFilePath)) return new List<RenoDxWikiMod>();

            try
            {
                return JsonConvert.DeserializeObject<List<RenoDxWikiMod>>(File.ReadAllText(cacheFilePath))
                    ?? new List<RenoDxWikiMod>();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read RenoDX wiki mod cache.");
                return new List<RenoDxWikiMod>();
            }
        }
    }
}
