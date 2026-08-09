using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using Newtonsoft.Json;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class OptiScalerWikiServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public OptiScalerWikiServiceTests()
        {
            Directory.CreateDirectory(directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        // Matches the real wiki table shape: Game Name | Working | FSR4 | Upscalers | Notes | Screenshots.
        private const string SampleHtml = @"
            <table>
                <tr><th>Game</th><th>Working</th><th>FSR4</th><th>Upscalers</th><th>Notes</th><th>Screenshots</th></tr>
                <tr>
                    <td><a href=""./Control"">Control</a></td>
                    <td>✔️</td>
                    <td></td>
                    <td>DLSS</td>
                    <td>Works great</td>
                    <td></td>
                </tr>
                <tr>
                    <td><a href=""/optiscaler/OptiScaler/wiki/Deep-Rock-Galactic"">Deep Rock Galactic</a></td>
                    <td>✔️</td>
                    <td>✅</td>
                    <td>DLSS, FSR2, FSR4, XeSS</td>
                    <td></td>
                    <td></td>
                </tr>
                <tr>
                    <td>Broken Game</td>
                    <td>❌</td>
                    <td></td>
                    <td></td>
                    <td>Doesn't launch</td>
                    <td></td>
                </tr>
            </table>";

        private static Dictionary<string, OptiScalerCompatEntry> Parse(string html)
        {
            var entries = new Dictionary<string, OptiScalerCompatEntry>();
            OptiScalerWikiService.ParseCompatibilityPage(html, entries);
            return entries;
        }

        [Fact]
        public void ParseCompatibilityPage_SkipsHeaderRow()
        {
            var entries = Parse(SampleHtml);

            Assert.DoesNotContain("Game", entries.Keys);
        }

        [Fact]
        public void ParseCompatibilityPage_DlssOnlyGame_ParsesSingleUpscaler()
        {
            var entries = Parse(SampleHtml);

            Assert.True(entries.TryGetValue("Control", out var entry));
            Assert.Equal(new[] { "DLSS" }, entry.Upscalers);
            Assert.Equal("Working", entry.Status);
            Assert.Equal("Works great", entry.Notes);
            Assert.Equal("https://github.com/optiscaler/OptiScaler/wiki/Control", entry.DetailPageUrl);
        }

        [Fact]
        public void ParseCompatibilityPage_MultiUpscalerGame_ParsesAllOfThem()
        {
            var entries = Parse(SampleHtml);

            Assert.True(entries.TryGetValue("Deep Rock Galactic", out var entry));
            Assert.Equal(new[] { "DLSS", "FSR2", "FSR4", "XeSS" }, entry.Upscalers);
            Assert.Equal("https://github.com/optiscaler/OptiScaler/wiki/Deep-Rock-Galactic", entry.DetailPageUrl);
        }

        [Fact]
        public void ParseCompatibilityPage_NoLinkAndNoUpscalers_StillParsesGameName()
        {
            var entries = Parse(SampleHtml);

            Assert.True(entries.TryGetValue("Broken Game", out var entry));
            Assert.Empty(entry.Upscalers);
            Assert.Equal("Not Working", entry.Status);
            Assert.Null(entry.DetailPageUrl);
        }

        [Fact]
        public void ParseCompatibilityPage_DuplicateGameName_FirstOccurrenceWins()
        {
            const string html = @"
                <table>
                    <tr><th>Game</th><th>Working</th><th>FSR4</th><th>Upscalers</th></tr>
                    <tr><td>Same Game</td><td>✔️</td><td></td><td>DLSS</td></tr>
                    <tr><td>Same Game</td><td>❌</td><td></td><td>FSR2</td></tr>
                </table>";

            var entries = Parse(html);

            Assert.Equal(new[] { "DLSS" }, entries["Same Game"].Upscalers);
        }

        [Fact]
        public void ParseCompatibilityPage_NoTables_ReturnsEmpty()
        {
            var entries = Parse("<div>no tables here</div>");

            Assert.Empty(entries);
        }

        [Fact]
        public void ParseCompatibilityPage_DifferentColumnOrder_StillFindsUpscalersByHeaderText()
        {
            // Regression for a real bug caught live 2026-08-09: the actual wiki page's column
            // order (Game, Compatibility, Upscaler Inputs, OptiPatcher Support, Notes,
            // Screenshots) doesn't match the fixed index this originally assumed from RHI's own
            // source comment (Game, Working, FSR4, Upscalers, Notes, Screenshots) — a real fetch
            // against Deep Rock Galactic returned "✨" (the OptiPatcher marker) instead of its
            // real upscaler list. Header-text lookup must resolve the right column regardless of
            // position.
            const string html = @"
                <table>
                    <tr><th>Game</th><th>Compatibility</th><th>Upscaler Inputs</th><th>OptiPatcher Support</th><th>Notes</th><th>Screenshots</th></tr>
                    <tr>
                        <td>Deep Rock Galactic</td>
                        <td>✔️</td>
                        <td>DLSS, FSR2</td>
                        <td>✨</td>
                        <td>Some note</td>
                        <td></td>
                    </tr>
                </table>";

            var entries = Parse(html);

            Assert.True(entries.TryGetValue("Deep Rock Galactic", out var entry));
            Assert.Equal(new[] { "DLSS", "FSR2" }, entry.Upscalers);
            Assert.Equal("Some note", entry.Notes);
        }

        [Fact]
        public void ResolveWikiName_NoOverride_ReturnsOriginalName()
        {
            var result = OptiScalerWikiService.ResolveWikiName("Control Ultimate Edition", manifest: null);

            Assert.Equal("Control Ultimate Edition", result);
        }

        [Fact]
        public void ResolveWikiName_ManifestHasOverride_ReturnsOverriddenName()
        {
            var manifest = new RemoteManifest
            {
                OptiScalerWikiNames = new Dictionary<string, string>
                {
                    ["Control Ultimate Edition"] = "Control"
                }
            };

            var result = OptiScalerWikiService.ResolveWikiName("Control Ultimate Edition", manifest);

            Assert.Equal("Control", result);
        }

        [Fact]
        public void ResolveWikiName_OverrideIsBlank_FallsBackToOriginalName()
        {
            var manifest = new RemoteManifest
            {
                OptiScalerWikiNames = new Dictionary<string, string>
                {
                    ["Some Game"] = "   "
                }
            };

            var result = OptiScalerWikiService.ResolveWikiName("Some Game", manifest);

            Assert.Equal("Some Game", result);
        }

        // ── Disk cache (FetchAsync's 24h gate) ──────────────────────────────────

        [Fact]
        public void TryGetCachedData_RoundTripsFetchedAtUtcAndEntries()
        {
            var fetchedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
            var onDisk = new OptiScalerWikiData { FetchedAtUtc = fetchedAt };
            onDisk.StandardCompat["Control"] = new OptiScalerCompatEntry { GameName = "Control", Upscalers = new List<string> { "DLSS" } };
            File.WriteAllText(Path.Combine(directory, "optiscaler_wiki_cache.json"), JsonConvert.SerializeObject(onDisk));

            var loaded = new OptiScalerWikiService(directory).TryGetCachedData();

            Assert.NotNull(loaded);
            Assert.Equal(fetchedAt, loaded.FetchedAtUtc);
            Assert.True(loaded.StandardCompat.TryGetValue("control", out var entry)); // case-insensitive
            Assert.Equal(new[] { "DLSS" }, entry.Upscalers);
        }

        [Fact]
        public void TryGetCachedData_NoCacheFileYet_ReturnsNull()
        {
            var loaded = new OptiScalerWikiService(directory).TryGetCachedData();

            Assert.Null(loaded);
        }
    }
}
