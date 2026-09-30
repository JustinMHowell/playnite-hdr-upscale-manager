using System.Collections.Generic;
using System.Linq;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    /* Wiki section tagging and the notes shown in the manager window — the wiki's layout here
       mirrors the live Mods page: a per-game table under "Legend", then engine compatibility
       tables under their own headings. */
    public class RenoDxWikiNotesTests
    {
        private const string WikiHtml = @"
            <h2>Legend</h2>
            <table>
                <tr><th>Name</th><th>Maintainer</th><th>Links</th><th>Status</th></tr>
                <tr>
                    <td>Atlas Fallen (DX12)</td>
                    <td>Akuru</td>
                    <td><a href='https://clshortfuse.github.io/renodx/renodx-atlasfallen.addon64'>Snapshot</a></td>
                    <td><a href='#' title=""Install ReShade in 'Atlas Fallen\bin'."">✅</a></td>
                </tr>
                <tr>
                    <td>Shared Game</td>
                    <td>Someone</td>
                    <td><a href='https://clshortfuse.github.io/renodx/renodx-shared.addon64'>Snapshot</a></td>
                    <td>🚧</td>
                </tr>
            </table>
            <h2>Multi-Game Mods</h2>
            <h3>Unreal Engine</h3>
            <table>
                <tr><th>Name</th><th>Status</th><th>Notes</th></tr>
                <tr><td>Astroneer</td><td>✅</td><td>Glitches <code>B8G8R8A8_TYPELESS</code> <code>Output Size</code></td></tr>
                <tr><td>Shared Game</td><td>✅</td><td>Enable Swapchain Proxy</td></tr>
            </table>
            <h3>Unity Engine</h3>
            <table>
                <tr><th>Name</th><th>Status</th><th>Notes</th></tr>
                <tr><td>7 Days to Die</td><td>✅</td><td>Enable Swapchain Proxy.</td></tr>
            </table>";

        private static List<RenoDxWikiMod> Mods => RenoDxWikiService.ParseMods(WikiHtml);

        [Fact]
        public void ParseMods_TagsRowsWithTheirSectionHeading()
        {
            var mods = Mods;

            Assert.Equal("Legend", mods.First(m => m.Name == "Atlas Fallen (DX12)").Section);
            Assert.Equal("Unreal Engine", mods.First(m => m.Name == "Astroneer").Section);
            Assert.Equal("Unity Engine", mods.First(m => m.Name == "7 Days to Die").Section);
            Assert.False(mods.First(m => m.Name == "Atlas Fallen (DX12)").IsEngineCompatEntry);
            Assert.True(mods.First(m => m.Name == "Astroneer").IsEngineCompatEntry);
        }

        [Fact]
        public void Match_GameOnlyInEngineList_ReturnsNull()
        {
            // Regression: an engine-list row used to match, carry no download, and stop
            // InstallRenoDxAsync before its generic engine fallback.
            Assert.Null(RenoDxGameMatcher.Match("Astroneer", Mods));
        }

        [Fact]
        public void Match_GameInBothLists_ReturnsTheInstallableMod()
        {
            var match = RenoDxGameMatcher.Match("Shared Game", Mods);

            Assert.NotNull(match);
            Assert.EndsWith("renodx-shared.addon64", match.SnapshotUrl);
        }

        [Fact]
        public void FindWikiNotes_PerGameMod_ReturnsItsNote()
        {
            var notes = RenoDxGameMatcher.FindWikiNotes("Atlas Fallen (DX12)", Mods);

            Assert.Equal(new[] { "Install ReShade in 'Atlas Fallen\\bin'." }, notes);
        }

        [Fact]
        public void FindWikiNotes_EngineListEntry_IsLabelledAndStripsCodeMarks()
        {
            var notes = RenoDxGameMatcher.FindWikiNotes("Astroneer", Mods);

            Assert.Equal(new[] { "Unreal Engine list: Glitches B8G8R8A8_TYPELESS Output Size" }, notes);
        }

        [Fact]
        public void FindWikiNotes_GameInBothLists_CombinesWorkInProgressAndEngineNotes()
        {
            var notes = RenoDxGameMatcher.FindWikiNotes("Shared Game", Mods);

            Assert.Equal(new[] { "Work in progress.", "Unreal Engine list: Enable Swapchain Proxy" }, notes);
        }

        [Fact]
        public void FindWikiNotes_UnlistedGame_ReturnsEmpty()
        {
            Assert.Empty(RenoDxGameMatcher.FindWikiNotes("Not A Real Game", Mods));
        }

        [Fact]
        public void IsEngineCompatEntry_NullSection_TreatedAsInstallableMod()
        {
            // Rows cached before Section existed must keep matching like they always did.
            Assert.False(new RenoDxWikiMod { Name = "Old", Section = null }.IsEngineCompatEntry);
        }
    }
}
