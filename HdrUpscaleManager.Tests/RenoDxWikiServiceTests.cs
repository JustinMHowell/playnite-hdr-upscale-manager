using System.Linq;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class RenoDxWikiServiceTests
    {
        [Fact]
        public void ParseMods_FourColumnTable_ParsesNameMaintainerLinkAndStatus()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Maintainer</th><th>Links</th><th>Status</th></tr>
                    <tr>
                        <td>Code Vein</td>
                        <td>SomeDev</td>
                        <td><a href='https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-codevein.addon64'>Snapshot</a></td>
                        <td>✅</td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.Equal("Code Vein", mod.Name);
            Assert.Equal("SomeDev", mod.Maintainer);
            Assert.EndsWith("renodx-codevein.addon64", mod.SnapshotUrl);
            Assert.False(mod.IsWorkInProgress);
        }

        [Fact]
        public void ParseMods_TableWithoutLinksOrStatusColumn_IsSkipped()
        {
            var html = @"
                <table>
                    <tr><th>Notice</th><th>Description</th></tr>
                    <tr><td>Heads up</td><td>This is just a page notice, not a mod table.</td></tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            Assert.Empty(mods);
        }

        [Fact]
        public void ParseMods_32BitOnlySnapshot_IsPromotedToPrimaryUrl()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Links</th><th>Status</th></tr>
                    <tr>
                        <td>Terraria</td>
                        <td><a href='https://example.com/renodx-terraria.addon32'>Snapshot</a></td>
                        <td>✅</td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.EndsWith(".addon32", mod.SnapshotUrl32);
            Assert.Equal(mod.SnapshotUrl32, mod.SnapshotUrl);
        }

        [Fact]
        public void ParseMods_WorkInProgressStatusWithTooltip_SetsFlagAndNotes()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Links</th><th>Status</th></tr>
                    <tr>
                        <td>Some Early Access Game</td>
                        <td><a href='https://example.com/renodx-early.addon64'>Snapshot</a></td>
                        <td><a title='Disable in-game HDR before enabling RenoDX' href='#'>🚧</a></td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.True(mod.IsWorkInProgress);
            Assert.Equal("Disable in-game HDR before enabling RenoDX", mod.Notes);
        }

        [Fact]
        public void ParseMods_ExtraNotesColumn_IsCapturedWhenStatusHasNoTooltip()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Maintainer</th><th>Links</th><th>Status</th><th>Notes</th></tr>
                    <tr>
                        <td>Lies of P</td>
                        <td>SomeDev</td>
                        <td><a href='https://example.com/renodx-liesofp.addon64'>Snapshot</a></td>
                        <td>✅</td>
                        <td>Requires the Windows HDR toggle to be off.</td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.Equal("Requires the Windows HDR toggle to be off.", mod.Notes);
        }

        [Fact]
        public void ParseMods_NexusAndDiscordLinks_AreCaptured()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Links</th><th>Status</th></tr>
                    <tr>
                        <td>Some Game</td>
                        <td>
                            <a href='https://www.nexusmods.com/somegame/mods/1'>Nexus</a>
                            <a href='https://discord.gg/abcdef'>Discord</a>
                        </td>
                        <td>✅</td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.Contains("nexusmods.com", mod.NexusUrl);
            Assert.Contains("discord.gg", mod.DiscordUrl);
            Assert.Null(mod.SnapshotUrl);
        }

        [Fact]
        public void ParseMods_MultipleTables_OnlyParsesModTables()
        {
            var html = @"
                <table>
                    <tr><th>Table of contents</th></tr>
                    <tr><td>Some link somewhere</td></tr>
                </table>
                <table>
                    <tr><th>Name</th><th>Links</th><th>Status</th></tr>
                    <tr>
                        <td>Game A</td>
                        <td><a href='https://example.com/a.addon64'>Snapshot</a></td>
                        <td>✅</td>
                    </tr>
                    <tr>
                        <td>Game B</td>
                        <td><a href='https://example.com/b.addon64'>Snapshot</a></td>
                        <td>✅</td>
                    </tr>
                </table>";

            var mods = RenoDxWikiService.ParseMods(html);

            Assert.Equal(2, mods.Count);
            Assert.Equal(new[] { "Game A", "Game B" }, mods.Select(m => m.Name));
        }
    }
}
