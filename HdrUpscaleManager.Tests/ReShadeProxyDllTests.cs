using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class ReShadeProxyDllTests
    {
        [Theory]
        [InlineData(GraphicsApi.D3D9, "d3d9.dll")]
        [InlineData(GraphicsApi.D3D10, "dxgi.dll")]
        [InlineData(GraphicsApi.D3D11, "dxgi.dll")]
        [InlineData(GraphicsApi.D3D12, "dxgi.dll")]
        [InlineData(GraphicsApi.OpenGl, "opengl32.dll")]
        public void GetProxyDllName_ReturnsExpectedName(GraphicsApi api, string expected)
        {
            Assert.Equal(expected, ReShadeProxyDll.GetProxyDllName(api));
        }

        [Theory]
        [InlineData(GraphicsApi.Vulkan)]
        [InlineData(GraphicsApi.Unknown)]
        public void GetProxyDllName_NoDropInProxy_ReturnsNull(GraphicsApi api)
        {
            Assert.Null(ReShadeProxyDll.GetProxyDllName(api));
        }
    }
}
