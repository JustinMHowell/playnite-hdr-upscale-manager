using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
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

        [Fact]
        public void DeployIni_WritesFreshFile_ReturnsTrue()
        {
            // InstallAsync uses this to decide whether "OptiScaler.ini" belongs in
            // InstalledFiles — a leftover on The Riftbreaker/Deep Rock Galactic's real install
            // folders (confirmed live 2026-08-09) showed Uninstall never removed it before this
            // fix, since it was never tracked in the first place.
            var wroteFresh = OptiScalerService.DeployIni(directory, dlssInputs: true);

            Assert.True(wroteFresh);
        }

        [Fact]
        public void DeployIni_ExistingIniPreserved_ReturnsFalse()
        {
            File.WriteAllText(Path.Combine(directory, "OptiScaler.ini"), "user's own customized ini");

            var wroteFresh = OptiScalerService.DeployIni(directory, dlssInputs: true);

            Assert.False(wroteFresh);
        }

        [Fact]
        public void Uninstall_OptiPatcherPluginsFolderNowEmpty_RemovesFolderToo()
        {
            // Confirmed as a real leftover live 2026-08-09 on Vanquish: Uninstall deleted
            // OptiPatcher.asi but left the now-empty "plugins" folder it was created in behind.
            var pluginsDir = Path.Combine(directory, "plugins");
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllText(Path.Combine(pluginsDir, "OptiPatcher.asi"), "asi bytes");

            var record = new OptiScalerInstalledRecord
            {
                GameId = "game-1",
                GameName = "Some Game",
                InstallPath = directory,
                DllName = "dxgi.dll",
                InstalledFiles = new List<string>(),
                OptiPatcherInstalled = true,
            };

            var result = OptiScalerService.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(Directory.Exists(pluginsDir));
        }

        [Fact]
        public void Uninstall_PluginsFolderHasOtherFiles_LeavesFolderInPlace()
        {
            // Another mod's ASI loader (or the game itself) might use the same "plugins" folder
            // — only remove it if OptiPatcher.asi was genuinely the only thing in it.
            var pluginsDir = Path.Combine(directory, "plugins");
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllText(Path.Combine(pluginsDir, "OptiPatcher.asi"), "asi bytes");
            File.WriteAllText(Path.Combine(pluginsDir, "SomeOtherMod.asi"), "other mod bytes");

            var record = new OptiScalerInstalledRecord
            {
                GameId = "game-1",
                GameName = "Some Game",
                InstallPath = directory,
                DllName = "dxgi.dll",
                InstalledFiles = new List<string>(),
                OptiPatcherInstalled = true,
            };

            var result = OptiScalerService.Uninstall(record);

            Assert.True(result.Success);
            Assert.True(Directory.Exists(pluginsDir));
            Assert.True(File.Exists(Path.Combine(pluginsDir, "SomeOtherMod.asi")));
        }
    }
}
