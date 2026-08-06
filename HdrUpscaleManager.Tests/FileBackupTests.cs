using System;
using System.IO;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class FileBackupTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public FileBackupTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Fact]
        public void BackupIfNeeded_NoExistingFile_ReturnsFalseAndCreatesNoBackup()
        {
            var targetPath = Path.Combine(directory, "d3d9.dll");

            var result = FileBackup.BackupIfNeeded(targetPath);

            Assert.False(result);
            Assert.False(File.Exists(targetPath + ".original"));
        }

        [Fact]
        public void BackupIfNeeded_ExistingFile_MovesItToOriginalAndReturnsTrue()
        {
            var targetPath = Path.Combine(directory, "d3d9.dll");
            File.WriteAllText(targetPath, "original game file");

            var result = FileBackup.BackupIfNeeded(targetPath);

            Assert.True(result);
            Assert.False(File.Exists(targetPath));
            Assert.Equal("original game file", File.ReadAllText(targetPath + ".original"));
        }

        [Fact]
        public void BackupIfNeeded_BackupAlreadyExists_LeavesExistingBackupAlone()
        {
            var targetPath = Path.Combine(directory, "d3d9.dll");
            File.WriteAllText(targetPath, "second install's file");
            File.WriteAllText(targetPath + ".original", "the real original");

            var result = FileBackup.BackupIfNeeded(targetPath);

            Assert.False(result);
            Assert.Equal("the real original", File.ReadAllText(targetPath + ".original"));
        }
    }
}
