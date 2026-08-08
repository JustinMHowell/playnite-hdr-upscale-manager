using System.Collections.Generic;
using HdrUpscaleManager.Services.Addons;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class PcGamingWikiServiceTests
    {
        [Theory]
        [InlineData("Engine:LS3D", "LS3D")]
        [InlineData("Engine:Unreal Engine 4, Engine:Havok", "Unreal Engine 4")]
        [InlineData("  Engine:Source  ", "Source")]
        [InlineData(null, null)]
        [InlineData("", null)]
        public void CleanEngineName_StripsNamespacePrefixAndTakesFirstListed(string raw, string expected)
        {
            Assert.Equal(expected, PcGamingWikiService.CleanEngineName(raw));
        }

        [Theory]
        [InlineData("Unreal Engine 4", RenoDxEngine.UnrealEngine)]
        [InlineData("unreal engine 5", RenoDxEngine.UnrealEngine)]
        [InlineData("Unity", RenoDxEngine.UnityEngine)]
        [InlineData("LS3D", null)]
        [InlineData(null, null)]
        public void MapEngine_RecognizesUnrealAndUnityCaseInsensitively(string engineName, RenoDxEngine? expected)
        {
            Assert.Equal(expected, PcGamingWikiService.MapEngine(engineName));
        }

        [Fact]
        public void ParseCargoList_SplitsAndTrimsCommaDelimitedValues()
        {
            var result = PcGamingWikiService.ParseCargoList(" 8.1 , 9.0c ,10 ");

            Assert.Equal(new[] { "8.1", "9.0c", "10" }, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseCargoList_BlankInput_ReturnsEmptyList(string raw)
        {
            Assert.Empty(PcGamingWikiService.ParseCargoList(raw));
        }

        [Fact]
        public void InferConfidentGraphicsApi_SingleD3D8Version_ReturnsD3D8()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApi(new List<string> { "8.1" });

            Assert.Equal(GraphicsApi.D3D8, result);
        }

        [Fact]
        public void InferConfidentGraphicsApi_MixOfD3D8And9_ReturnsHighestTier()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApi(new List<string> { "8.1", "9.0c" });

            Assert.Equal(GraphicsApi.D3D9, result);
        }

        [Fact]
        public void InferConfidentGraphicsApi_IncludesD3D11_ReturnsNull()
        {
            // e.g. BioShock: base DX9 with an optional DX10 path shipped later — but a game that
            // ever lists 11/12 can't be confidently recommended without knowing which path an
            // actual installed executable would load.
            var result = PcGamingWikiService.InferConfidentGraphicsApi(new List<string> { "9.0c", "10", "11" });

            Assert.Null(result);
        }

        [Fact]
        public void InferConfidentGraphicsApi_EmptyList_ReturnsNull()
        {
            Assert.Null(PcGamingWikiService.InferConfidentGraphicsApi(new List<string>()));
        }

        [Fact]
        public void InferConfidentGraphicsApi_UnrecognizedVersionString_ReturnsNull()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApi(new List<string> { "n/a" });

            Assert.Null(result);
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_SingleD3D11Version_ReturnsD3D11()
        {
            // Hollow Knight's real PCGamingWiki entry, confirmed live 2026-08-08 — a single
            // unambiguous "11" that InferConfidentGraphicsApi (DXVK-scoped, ≤10 ceiling) wrongly
            // discards as "not confident" even though there's nothing ambiguous about it.
            var result = PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string> { "11" });

            Assert.Equal(GraphicsApi.D3D11, result);
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_SingleD3D12Version_ReturnsD3D12()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string> { "12" });

            Assert.Equal(GraphicsApi.D3D12, result);
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_MultipleDistinctVersions_ReturnsNull()
        {
            // Genuinely ambiguous — a game offering more than one selectable render path with
            // no installed executable to say which one actually loads.
            var result = PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string> { "9.0c", "11" });

            Assert.Null(result);
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_SameVersionRepeated_ReturnsThatVersion()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string> { "11", "11" });

            Assert.Equal(GraphicsApi.D3D11, result);
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_EmptyList_ReturnsNull()
        {
            Assert.Null(PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string>()));
        }

        [Fact]
        public void InferConfidentGraphicsApiForReShade_UnrecognizedVersionString_ReturnsNull()
        {
            var result = PcGamingWikiService.InferConfidentGraphicsApiForReShade(new List<string> { "n/a" });

            Assert.Null(result);
        }
    }
}
