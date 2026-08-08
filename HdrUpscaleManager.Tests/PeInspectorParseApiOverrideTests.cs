using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class PeInspectorParseApiOverrideTests
    {
        [Theory]
        [InlineData("DX8", GraphicsApi.D3D8)]
        [InlineData("DX9", GraphicsApi.D3D9)]
        [InlineData("DX10", GraphicsApi.D3D10)]
        [InlineData("DX11", GraphicsApi.D3D11)]
        [InlineData("DX12", GraphicsApi.D3D12)]
        [InlineData("OGL", GraphicsApi.OpenGl)]
        [InlineData("VLK", GraphicsApi.Vulkan)]
        [InlineData("dx12", GraphicsApi.D3D12)]
        public void ParseApiOverride_SingleKnownToken_ReturnsMappedApi(string raw, GraphicsApi expected)
        {
            Assert.Equal(expected, PeInspector.ParseApiOverride(raw));
        }

        [Theory]
        [InlineData("DX11, DX12", GraphicsApi.D3D12)]
        [InlineData("DX12, VLK", GraphicsApi.D3D12)]
        public void ParseApiOverride_MultipleTokens_PicksHighestPriorityApi(string raw, GraphicsApi expected)
        {
            Assert.Equal(expected, PeInspector.ParseApiOverride(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a real api")]
        public void ParseApiOverride_NullOrUnrecognized_ReturnsNull(string raw)
        {
            Assert.Null(PeInspector.ParseApiOverride(raw));
        }
    }
}
