using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class LumaServiceUninstallTests : IDisposable
    {
        private readonly string gameInstallDirectory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public LumaServiceUninstallTests() => Directory.CreateDirectory(gameInstallDirectory);

        public void Dispose()
        {
            if (Directory.Exists(gameInstallDirectory)) Directory.Delete(gameInstallDirectory, recursive: true);
        }

        private void WriteInstalledFile(string relativePath)
        {
            var fullPath = Path.Combine(gameInstallDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, "luma file");
        }

        [Fact]
        public void Uninstall_FilesNestedInSubfolders_RemovesFilesAndTheNowEmptySubfolders()
        {
            var relativePaths = new List<string>
            {
                "Luma/d3dcompiler_47.dll",
                "Luma/Global/Luma_Copy_PS.hlsl",
                "Luma/Includes/Common.hlsl",
                "Luma/INSIDE/Lighting_Phase2_0xDD83BF95.ps_4_0.hlsl",
                "Luma/INSIDE/Includes/Common.hlsl",
            };
            foreach (var path in relativePaths) WriteInstalledFile(path);

            var record = new LumaInstalledRecord
            {
                GameName = "INSIDE",
                InstallPath = gameInstallDirectory,
                InstalledFiles = relativePaths
            };

            var result = LumaService.Uninstall(record);

            Assert.True(result.Success);
            foreach (var path in relativePaths)
            {
                Assert.False(File.Exists(Path.Combine(gameInstallDirectory, path)), $"'{path}' should have been deleted.");
            }
            Assert.False(Directory.Exists(Path.Combine(gameInstallDirectory, "Luma")),
                "The 'Luma' directory tree Uninstall created should be fully removed once every file under it is gone.");
        }

        [Fact]
        public void Uninstall_SharedDirectoryStillHasUntrackedFile_LeavesThatDirectoryInPlace()
        {
            WriteInstalledFile("Luma/Global/Luma_Copy_PS.hlsl");
            /* Not tracked by this record — simulates a file the game itself put there, or a
               second addon sharing the same subfolder. Uninstall must never remove a directory
               it didn't empty out itself. */
            File.WriteAllText(Path.Combine(gameInstallDirectory, "Luma", "Global", "not-ours.txt"), "keep me");

            var record = new LumaInstalledRecord
            {
                GameName = "INSIDE",
                InstallPath = gameInstallDirectory,
                InstalledFiles = new List<string> { "Luma/Global/Luma_Copy_PS.hlsl" }
            };

            var result = LumaService.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(gameInstallDirectory, "Luma", "Global", "Luma_Copy_PS.hlsl")));
            Assert.True(File.Exists(Path.Combine(gameInstallDirectory, "Luma", "Global", "not-ours.txt")),
                "A file Uninstall never tracked must survive.");
            Assert.True(Directory.Exists(Path.Combine(gameInstallDirectory, "Luma", "Global")),
                "A directory that still has an untracked file in it must not be deleted.");
        }

        [Fact]
        public void Uninstall_FileDirectlyInGameRoot_NeverDeletesTheGameInstallDirectoryItself()
        {
            WriteInstalledFile("dxgi.dll");

            var record = new LumaInstalledRecord
            {
                GameName = "Vanquish",
                InstallPath = gameInstallDirectory,
                InstalledFiles = new List<string> { "dxgi.dll" }
            };

            var result = LumaService.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(gameInstallDirectory, "dxgi.dll")));
            Assert.True(Directory.Exists(gameInstallDirectory), "The game's own install directory must never be deleted.");
        }
    }
}
