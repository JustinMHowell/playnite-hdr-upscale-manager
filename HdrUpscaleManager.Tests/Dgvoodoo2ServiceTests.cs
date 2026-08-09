using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class Dgvoodoo2ServiceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public Dgvoodoo2ServiceTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        // ── ResolveDllName / ResolveBitnessFolder (pure, no network) ────────────

        [Theory]
        [InlineData(GraphicsApi.D3D8, "D3D8.dll")]
        [InlineData(GraphicsApi.D3D9, "D3D9.dll")]
        public void ResolveDllName_SupportedApi_ReturnsExpectedDll(GraphicsApi api, string expected)
        {
            Assert.Equal(expected, Dgvoodoo2Service.ResolveDllName(api));
        }

        [Theory]
        [InlineData(GraphicsApi.Unknown)]
        [InlineData(GraphicsApi.D3D10)]
        [InlineData(GraphicsApi.D3D11)]
        [InlineData(GraphicsApi.D3D12)]
        [InlineData(GraphicsApi.OpenGl)]
        [InlineData(GraphicsApi.Vulkan)]
        public void ResolveDllName_UnsupportedApi_ReturnsNull(GraphicsApi api)
        {
            // dgVoodoo2 only wraps DirectDraw/Direct3D up to version 9 — Vanquish's own case
            // (D3D9) is covered above; everything from D3D10 up (and non-Direct3D APIs) has no
            // real dgVoodoo2 path and must be rejected, not silently mapped to the wrong DLL.
            Assert.Null(Dgvoodoo2Service.ResolveDllName(api));
        }

        [Theory]
        [InlineData(true, "x64")]
        [InlineData(false, "x86")]
        public void ResolveBitnessFolder_ReturnsExpectedFolder(bool is64Bit, string expected)
        {
            Assert.Equal(expected, Dgvoodoo2Service.ResolveBitnessFolder(is64Bit));
        }

        private string WriteFile(string name, string content = "content")
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Uninstall_RemovesInstalledFiles()
        {
            WriteFile("D3D9.dll", "dgvoodoo dll bytes");
            WriteFile("dgVoodoo.conf", "conf contents");

            var record = new Dgvoodoo2InstalledRecord
            {
                GameId = "game-1",
                GameName = "Vanquish",
                InstallPath = directory,
                InstalledFiles = new List<string> { "D3D9.dll", "dgVoodoo.conf" },
                BackedUpFiles = new List<string>(),
            };

            var result = Dgvoodoo2Service.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "D3D9.dll")));
            Assert.False(File.Exists(Path.Combine(directory, "dgVoodoo.conf")));
        }

        [Fact]
        public void Uninstall_BackedUpFile_RestoresOriginal()
        {
            WriteFile("D3D9.dll", "dgvoodoo dll bytes");
            WriteFile("D3D9.dll.original", "the game's real D3D9.dll");

            var record = new Dgvoodoo2InstalledRecord
            {
                GameId = "game-1",
                GameName = "Vanquish",
                InstallPath = directory,
                InstalledFiles = new List<string> { "D3D9.dll" },
                BackedUpFiles = new List<string> { "D3D9.dll" },
            };

            var result = Dgvoodoo2Service.Uninstall(record);

            Assert.True(result.Success);
            var restoredPath = Path.Combine(directory, "D3D9.dll");
            Assert.True(File.Exists(restoredPath));
            Assert.Equal("the game's real D3D9.dll", File.ReadAllText(restoredPath));
            Assert.False(File.Exists(restoredPath + ".original"));
        }

        [Fact]
        public void Uninstall_NotBackedUp_LeavesNoBackupFileBehind()
        {
            WriteFile("D3D9.dll", "dgvoodoo dll bytes");

            var record = new Dgvoodoo2InstalledRecord
            {
                GameId = "game-1",
                GameName = "Vanquish",
                InstallPath = directory,
                InstalledFiles = new List<string> { "D3D9.dll" },
                BackedUpFiles = new List<string>(),
            };

            var result = Dgvoodoo2Service.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "D3D9.dll")));
            Assert.False(File.Exists(Path.Combine(directory, "D3D9.dll.original")));
        }

        [Fact]
        public void Uninstall_NoInstalledFiles_StillSucceeds()
        {
            var record = new Dgvoodoo2InstalledRecord
            {
                GameId = "game-1",
                GameName = "Vanquish",
                InstallPath = directory,
                InstalledFiles = null,
                BackedUpFiles = null,
            };

            var result = Dgvoodoo2Service.Uninstall(record);

            Assert.True(result.Success);
        }
    }
}
