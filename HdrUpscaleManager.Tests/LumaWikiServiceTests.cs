using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class LumaWikiServiceTests
    {
        [Fact]
        public void ParseMods_NameAuthorDownloadStatusNotesTable_ParsesAllFields()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Author</th><th>Download Link</th><th>Status</th><th>Special Notes</th></tr>
                    <tr>
                        <td>Code Vein</td>
                        <td>Filoppi</td>
                        <td><a href='https://github.com/Filoppi/Luma-Framework/releases/download/latest/codevein.zip'>Download</a></td>
                        <td>✅</td>
                        <td>Works great with native HDR.</td>
                    </tr>
                </table>";

            var mods = LumaWikiService.ParseMods(html);

            var mod = Assert.Single(mods);
            Assert.Equal("Code Vein", mod.Name);
            Assert.Equal("Filoppi", mod.Author);
            Assert.EndsWith("codevein.zip", mod.DownloadUrl);
            Assert.Equal("✅", mod.Status);
            Assert.Equal("Works great with native HDR.", mod.SpecialNotes);
        }

        [Fact]
        public void ParseMods_WorkInProgressEmoji_SetsWipStatus()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Download Link</th><th>Status</th></tr>
                    <tr>
                        <td>Some Game</td>
                        <td><a href='https://github.com/Filoppi/Luma-Framework/releases/download/latest/somegame.zip'>Download</a></td>
                        <td>🚧</td>
                    </tr>
                </table>";

            var mods = LumaWikiService.ParseMods(html);

            Assert.Equal("🚧", Assert.Single(mods).Status);
        }

        [Fact]
        public void ParseMods_TableWithoutNameOrDownloadColumn_IsSkipped()
        {
            var html = @"
                <table>
                    <tr><th>Notice</th><th>Description</th></tr>
                    <tr><td>Heads up</td><td>Not a mod table.</td></tr>
                </table>";

            var mods = LumaWikiService.ParseMods(html);

            Assert.Empty(mods);
        }

        [Fact]
        public void ParseMods_NoDownloadLinkInRow_LeavesDownloadUrlNull()
        {
            var html = @"
                <table>
                    <tr><th>Name</th><th>Download Link</th><th>Status</th></tr>
                    <tr>
                        <td>Coming Soon Game</td>
                        <td>TBD</td>
                        <td>✅</td>
                    </tr>
                </table>";

            var mods = LumaWikiService.ParseMods(html);

            Assert.Null(Assert.Single(mods).DownloadUrl);
        }
    }
}
