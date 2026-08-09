using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Services.Addons;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class OptiScalerServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public OptiScalerServiceTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Fact]
        public void ResolveDllName_Vulkan_DefaultsToWinmm()
        {
            Assert.Equal("winmm.dll", OptiScalerService.ResolveDllName("Some Game", GraphicsApi.Vulkan, null));
        }

        [Theory]
        [InlineData(GraphicsApi.D3D11)]
        [InlineData(GraphicsApi.D3D12)]
        public void ResolveDllName_NonVulkan_DefaultsToDxgi(GraphicsApi api)
        {
            Assert.Equal("dxgi.dll", OptiScalerService.ResolveDllName("Some Game", api, null));
        }

        [Fact]
        public void ResolveDllName_ManifestOverride_TakesPriorityOverVulkanDefault()
        {
            var manifest = new RemoteManifest
            {
                OptiScalerDllOverrides = new Dictionary<string, string> { ["Mirror's Edge"] = "d3d12.dll" }
            };

            var name = OptiScalerService.ResolveDllName("Mirror's Edge", GraphicsApi.Vulkan, manifest);

            Assert.Equal("d3d12.dll", name);
        }

        [Fact]
        public void ReplaceExactLine_MatchesOnlyExactTrimmedLine()
        {
            var content = "Dxgi=auto\nDxgiVRAM=auto\nOther=1";

            var result = OptiScalerService.ReplaceExactLine(content, "Dxgi=auto", "Dxgi=false");

            Assert.Contains("Dxgi=false", result);
            Assert.Contains("DxgiVRAM=auto", result); // untouched despite sharing "=auto"
        }

        [Fact]
        public void ReplaceExactLine_NoMatch_ReturnsContentUnchanged()
        {
            var content = "Foo=auto";

            var result = OptiScalerService.ReplaceExactLine(content, "Bar=auto", "Bar=false");

            Assert.Equal(content, result);
        }

        [Fact]
        public void DeployIni_DlssInputsTrue_LeavesDxgiSpoofingAuto()
        {
            OptiScalerService.DeployIni(directory, dlssInputs: true);

            var content = File.ReadAllText(Path.Combine(directory, "OptiScaler.ini"));
            Assert.Contains("Dxgi=auto", content);
        }

        [Fact]
        public void DeployIni_DlssInputsFalse_ForcesDxgiSpoofingOff()
        {
            OptiScalerService.DeployIni(directory, dlssInputs: false);

            var content = File.ReadAllText(Path.Combine(directory, "OptiScaler.ini"));
            Assert.Contains("Dxgi=false", content);
        }

        [Fact]
        public void DeployIni_AlwaysForcesReshadeAndAsiPluginLoadingAndShortcutKey()
        {
            OptiScalerService.DeployIni(directory, dlssInputs: true);

            var content = File.ReadAllText(Path.Combine(directory, "OptiScaler.ini"));
            Assert.Contains("LoadReshade=true", content);
            Assert.Contains("LoadAsiPlugins=true", content);
            Assert.Contains("ShortcutKey=0x2D", content);
        }

        [Fact]
        public void DeployIni_AlwaysForcesLogToFile_SoOptiScalerLogExistsAfterARealLaunch()
        {
            OptiScalerService.DeployIni(directory, dlssInputs: true);

            var content = File.ReadAllText(Path.Combine(directory, "OptiScaler.ini"));
            Assert.Contains("LogToFile=true", content);
        }

        [Fact]
        public void DeployIni_ExistingIniPresent_IsPreservedNotOverwritten()
        {
            var iniPath = Path.Combine(directory, "OptiScaler.ini");
            File.WriteAllText(iniPath, "user's own customized ini");

            OptiScalerService.DeployIni(directory, dlssInputs: true);

            Assert.Equal("user's own customized ini", File.ReadAllText(iniPath));
        }
    }
}
