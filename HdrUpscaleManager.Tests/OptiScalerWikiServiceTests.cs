using System.Collections.Generic;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class OptiScalerWikiServiceTests
    {
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
    }
}
