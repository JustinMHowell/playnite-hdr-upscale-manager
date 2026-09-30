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

        [Fact]
        public void BackupIfNeeded_OwnFile_IsNotBackedUp()
        {
            // Regression for Atomic Heart (confirmed live 2026-09-30): backing up the addon's own
            // earlier file made uninstall restore it instead of removing it.
            var targetPath = Path.Combine(directory, "dxgi.dll");
            File.WriteAllText(targetPath, "optiscaler from an earlier install");

            var result = FileBackup.BackupIfNeeded(targetPath, _ => true);

            Assert.False(result);
            Assert.True(File.Exists(targetPath));
            Assert.False(File.Exists(targetPath + ".original"));
        }

        [Fact]
        public void FilesEqual_ComparesContent()
        {
            var a = Path.Combine(directory, "a.dll");
            var b = Path.Combine(directory, "b.dll");
            var c = Path.Combine(directory, "c.dll");
            File.WriteAllText(a, "same bytes");
            File.WriteAllText(b, "same bytes");
            File.WriteAllText(c, "diff bytes");

            Assert.True(FileBackup.FilesEqual(a, b));
            Assert.False(FileBackup.FilesEqual(a, c));
            Assert.False(FileBackup.FilesEqual(a, Path.Combine(directory, "missing.dll")));
        }
    }
}
