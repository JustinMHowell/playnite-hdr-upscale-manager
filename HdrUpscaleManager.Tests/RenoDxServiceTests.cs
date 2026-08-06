using System.Collections.Generic;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class RenoDxServiceTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ResolveGenericAddon_UnrealEngine_AlwaysReturns64Bit(bool is64Bit)
        {
            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnrealEngine, is64Bit);

            Assert.Equal("renodx-unrealengine.addon64", addon.FileName);
            Assert.EndsWith(".addon64", addon.Url);
        }

        [Fact]
        public void ResolveGenericAddon_UnityEngine64Bit_ReturnsAddon64()
        {
            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnityEngine, is64Bit: true);

            Assert.Equal("renodx-unityengine.addon64", addon.FileName);
            Assert.EndsWith(".addon64", addon.Url);
        }

        [Fact]
        public void ResolveGenericAddon_UnityEngine32Bit_ReturnsAddon32()
        {
            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnityEngine, is64Bit: false);

            Assert.Equal("renodx-unityengine.addon32", addon.FileName);
            Assert.EndsWith(".addon32", addon.Url);
        }

        [Fact]
        public void ResolveGenericAddon_UnrealEngineUeExtended_ReturnsUeExtendedAddon()
        {
            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnrealEngine, is64Bit: true, useUeExtended: true);

            Assert.Equal(RenoDxService.UeExtendedAddonFileName, addon.FileName);
            Assert.Contains("ue-extended", addon.Url);
        }

        [Fact]
        public void ResolveGenericAddon_UeExtendedWithManifestOverride_UsesOverrideUrl()
        {
            var manifest = new RemoteManifest
            {
                ComponentUrls = new Dictionary<string, string> { ["ueExtended"] = "https://example.com/custom-ue-ext.addon64" }
            };

            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnrealEngine, is64Bit: true, useUeExtended: true, manifest: manifest);

            Assert.Equal("https://example.com/custom-ue-ext.addon64", addon.Url);
        }

        [Fact]
        public void ResolveGenericAddon_UnityEngineIgnoresUeExtendedFlag()
        {
            var addon = RenoDxService.ResolveGenericAddon(RenoDxEngine.UnityEngine, is64Bit: true, useUeExtended: true);

            Assert.Equal("renodx-unityengine.addon64", addon.FileName);
        }

        [Fact]
        public void ShouldUseUeExtended_NullManifest_DefaultsTrue()
        {
            Assert.True(RenoDxService.ShouldUseUeExtended("Any Game", null));
        }

        [Fact]
        public void ShouldUseUeExtended_NoOverrides_DefaultsTrue()
        {
            var manifest = new RemoteManifest();

            Assert.True(RenoDxService.ShouldUseUeExtended("Some Unreal Game", manifest));
        }

        [Fact]
        public void ShouldUseUeExtended_NoUeExtendedGames_ReturnsFalse()
        {
            var manifest = new RemoteManifest { NoUeExtendedGames = new List<string> { "Picky Game" } };

            Assert.False(RenoDxService.ShouldUseUeExtended("Picky Game", manifest));
        }

        [Fact]
        public void ShouldUseUeExtended_NativeHdrGameBeatsNoUeExtendedGames()
        {
            var manifest = new RemoteManifest
            {
                NativeHdrGames = new List<string> { "Returnal" },
                NoUeExtendedGames = new List<string> { "Returnal" }
            };

            Assert.True(RenoDxService.ShouldUseUeExtended("Returnal", manifest));
        }
    }
}
