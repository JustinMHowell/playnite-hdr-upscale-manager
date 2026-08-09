using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
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
