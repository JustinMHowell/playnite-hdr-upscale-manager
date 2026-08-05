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
    }
}
