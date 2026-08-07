using System.Collections.Generic;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class RenoDxGameMatcherTests
    {
        [Theory]
        [InlineData("Code Vein", "codevein")]
        [InlineData("Code Vein™", "codevein")]
        [InlineData("Elden Ring®", "eldenring")]
        [InlineData("Déjà Vu", "dejavu")]
        [InlineData("It Takes Two: Friend's Pass", "ittakestwofriendspass")]
        public void NormalizeName_StripsSymbolsDiacriticsAndCase(string input, string expected)
        {
            Assert.Equal(expected, RenoDxGameMatcher.NormalizeName(input));
        }

        [Fact]
        public void Match_ExactNormalizedMatch_ReturnsMod()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Code Vein", SnapshotUrl = "https://example.com/codevein.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Code Vein", mods);

            Assert.NotNull(match);
            Assert.Equal("Code Vein", match.Name);
        }

        [Fact]
        public void Match_GameNameContainsModName_PicksLongestFit()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Code Vein", SnapshotUrl = "https://example.com/a.addon64" },
                new RenoDxWikiMod { Name = "Code Vein II", SnapshotUrl = "https://example.com/b.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Code Vein II - Game of the Year Edition", mods);

            Assert.Equal("Code Vein II", match.Name);
        }

        [Fact]
        public void Match_ModNameContainsGameNameAsPrefix_ReturnsMod()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Lies of P", SnapshotUrl = "https://example.com/liesofp.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Lies of P", mods);

            Assert.NotNull(match);
        }

        [Fact]
        public void Match_RejectsSequelSuffixWhenGameNameIsShorterPrefix()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Doom 3", SnapshotUrl = "https://example.com/doom3.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Doom", mods);

            Assert.Null(match);
        }

        [Fact]
        public void Match_RejectsUnrelatedTitleSuffixWhenGameNameIsShorterPrefix()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Hollow Knight: Silksong", SnapshotUrl = "https://example.com/silksong.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Hollow Knight", mods);

            Assert.Null(match);
        }

        [Fact]
        public void Match_AcceptsKnownEditionSuffixWhenGameNameIsShorterPrefix()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Ori and the Blind Forest: Definitive Edition", SnapshotUrl = "https://example.com/ori.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Ori and the Blind Forest", mods);

            Assert.NotNull(match);
        }

        [Fact]
        public void Match_RejectsShortCandidateNameFoundAsRawSubstring()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Fe", SnapshotUrl = "https://example.com/fe.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Mass Effect 2", mods);

            Assert.Null(match);
        }

        [Fact]
        public void Match_RejectsCandidateNameFoundAsRawSubstringInsideALongerWord()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Stray", SnapshotUrl = "https://example.com/stray.addon64" }
            };

            var match = RenoDxGameMatcher.Match("MO:Astray", mods);

            Assert.Null(match);
        }

        [Fact]
        public void Match_NoCandidateFound_ReturnsNull()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Some Other Game", SnapshotUrl = "https://example.com/x.addon64" }
            };

            var match = RenoDxGameMatcher.Match("Completely Unrelated Title", mods);

            Assert.Null(match);
        }

        [Fact]
        public void Match_NameOverrideTakesPriorityOverAutoMatch()
        {
            var mods = new List<RenoDxWikiMod>
            {
                new RenoDxWikiMod { Name = "Correct Wiki Title", SnapshotUrl = "https://example.com/a.addon64" },
                new RenoDxWikiMod { Name = "Weird Detected Name", SnapshotUrl = "https://example.com/b.addon64" }
            };
            var overrides = new Dictionary<string, string> { { "Weird Detected Name", "Correct Wiki Title" } };

            var match = RenoDxGameMatcher.Match("Weird Detected Name", mods, overrides);

            Assert.Equal("Correct Wiki Title", match.Name);
        }
    }
}
