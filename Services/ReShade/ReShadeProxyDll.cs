namespace HdrUpscaleManager.Services.ReShade
{
    /* Which file name ReShade needs to be renamed to for a given graphics API to get loaded
       by the game. Vulkan has no drop-in proxy — it needs the system-wide implicit layer instead. */
    public static class ReShadeProxyDll
    {
        public static string GetProxyDllName(GraphicsApi api)
        {
            switch (api)
            {
                case GraphicsApi.D3D9:
                    return "d3d9.dll";
                case GraphicsApi.D3D10:
                case GraphicsApi.D3D11:
                case GraphicsApi.D3D12:
                    return "dxgi.dll";
                case GraphicsApi.OpenGl:
                    return "opengl32.dll";
                default:
                    return null;
            }
        }
    }
}
