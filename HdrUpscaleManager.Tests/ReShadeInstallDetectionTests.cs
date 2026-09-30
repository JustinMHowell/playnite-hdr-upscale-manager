using System;
using System.IO;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    /* Manual ReShade installs on games whose API reads as unknown — the placeholder game.exe
       below isn't a real PE file, so PeInspector.Inspect returns null and no apiOverride is
       passed, same as KnownGraphicsApiOrNull being null in ManagerViewModel. */
    public class ReShadeInstallDetectionTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public ReShadeInstallDetectionTests()
        {
            Directory.CreateDirectory(directory);
            WriteFile("game.exe", "exe");
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        private void WriteFile(string name, string content = "content") =>
            File.WriteAllText(Path.Combine(directory, name), content);

        [Theory]
        [InlineData("dxgi.dll")]
        [InlineData("d3d9.dll")]
        [InlineData("d3d11.dll")]
        [InlineData("opengl32.dll")]
        public void IsInstalled_UnknownApi_ManualInstallWithReShadeIni_ReturnsTrue(string proxyName)
        {
            WriteFile(proxyName);
            WriteFile("ReShade.ini");

            Assert.True(ReShadeInstallService.IsInstalled(directory));
        }

        [Fact]
        public void IsInstalled_UnknownApi_UnverifiedDxgiDll_ReturnsFalse()
        {
            // Could just as well be DXVK's or the game's own dxgi.dll.
            WriteFile("dxgi.dll");

            Assert.False(ReShadeInstallService.IsInstalled(directory));
        }

        [Fact]
        public void IsInstalled_KnownApi_ManualInstallUnderDifferentName_ReturnsTrue()
        {
            // ReShade's setup offers d3d11.dll for DX11 games, while this addon uses dxgi.dll.
            WriteFile("d3d11.dll");
            WriteFile("ReShade.ini");

            Assert.True(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        [Fact]
        public void Uninstall_UnknownApi_RemovesVerifiedManualInstall()
        {
            WriteFile("dxgi.dll", "reshade bytes");
            WriteFile("ReShade.ini");

            var result = ReShadeInstallService.Uninstall(directory);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "dxgi.dll")));
        }

        [Fact]
        public void Uninstall_UnknownApi_LeavesUnverifiedDllAlone()
        {
            WriteFile("dxgi.dll", "dxvk bytes");

            var result = ReShadeInstallService.Uninstall(directory);

            Assert.False(result.Success);
            Assert.True(File.Exists(Path.Combine(directory, "dxgi.dll")));
        }

        [Fact]
        public void LooksLikeReShade_PlainDllWithoutIni_ReturnsFalse()
        {
            WriteFile("dxgi.dll");

            Assert.False(ReShadeInstallService.LooksLikeReShade(Path.Combine(directory, "dxgi.dll")));
        }
    }
}
