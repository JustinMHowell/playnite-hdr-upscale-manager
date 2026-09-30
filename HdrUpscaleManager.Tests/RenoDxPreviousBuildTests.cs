using System;
using System.IO;
using System.Text;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    /* Keeping and swapping back to the RenoDX build an install replaced — regression cover for
       the 2026-09-30 UE-Extended snapshot that crashed Atomic Heart on startup. */
    public class RenoDxPreviousBuildTests : IDisposable
    {
        private const string Addon = "renodx-ue-extended.addon64";

        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public RenoDxPreviousBuildTests()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "game.exe"), "exe"); // ResolveDeployDirectory needs an exe
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        private string LivePath => Path.Combine(directory, Addon);
        private string PreviousPath => LivePath + AddonInstallService.PreviousBuildSuffix;

        [Fact]
        public void KeepPreviousBuild_DifferentDownload_KeepsReplacedFile()
        {
            File.WriteAllText(LivePath, "sep 29 build");

            AddonInstallService.KeepPreviousBuild(LivePath, Encoding.UTF8.GetBytes("sep 30 build"));

            Assert.Equal("sep 29 build", File.ReadAllText(PreviousPath));
        }

        [Fact]
        public void KeepPreviousBuild_IdenticalDownload_LeavesOlderBackupAlone()
        {
            File.WriteAllText(LivePath, "sep 30 build");
            File.WriteAllText(PreviousPath, "sep 29 build");

            AddonInstallService.KeepPreviousBuild(LivePath, File.ReadAllBytes(LivePath));

            Assert.Equal("sep 29 build", File.ReadAllText(PreviousPath));
        }

        [Fact]
        public void KeepPreviousBuild_FirstInstall_KeepsNothing()
        {
            AddonInstallService.KeepPreviousBuild(LivePath, Encoding.UTF8.GetBytes("sep 30 build"));

            Assert.False(File.Exists(PreviousPath));
        }

        [Fact]
        public void SwapToPreviousBuild_SwapsAndSwapsBack()
        {
            File.WriteAllText(LivePath, "sep 30 build");
            File.WriteAllText(PreviousPath, "sep 29 build");

            var first = RenoDxService.SwapToPreviousBuild(directory);

            Assert.True(first.Success);
            Assert.Equal(new[] { Addon }, first.SwappedFiles);
            Assert.Equal("sep 29 build", File.ReadAllText(LivePath));
            Assert.Equal("sep 30 build", File.ReadAllText(PreviousPath));

            RenoDxService.SwapToPreviousBuild(directory);

            Assert.Equal("sep 30 build", File.ReadAllText(LivePath));
            Assert.Equal("sep 29 build", File.ReadAllText(PreviousPath));
        }

        [Fact]
        public void SwapToPreviousBuild_NothingKept_Fails()
        {
            File.WriteAllText(LivePath, "sep 30 build");

            Assert.False(RenoDxService.SwapToPreviousBuild(directory).Success);
            Assert.Empty(RenoDxService.FindPreviousBuilds(directory));
        }

        [Fact]
        public void FindPreviousBuilds_IgnoresNonRenoDxFiles()
        {
            File.WriteAllText(Path.Combine(directory, "dxgi.dll" + AddonInstallService.PreviousBuildSuffix), "x");

            Assert.Empty(RenoDxService.FindPreviousBuilds(directory));
        }
    }
}
