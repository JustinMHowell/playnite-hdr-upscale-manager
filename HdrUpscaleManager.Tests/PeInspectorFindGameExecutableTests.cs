using System;
using System.IO;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class PeInspectorFindGameExecutableTests : IDisposable
    {
        private readonly string installDirectory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public PeInspectorFindGameExecutableTests() => Directory.CreateDirectory(installDirectory);

        public void Dispose()
        {
            if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, recursive: true);
        }

        private string WriteFile(string relativePath, int sizeInBytes)
        {
            var fullPath = Path.Combine(installDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, new byte[sizeInBytes]);
            return fullPath;
        }

        [Fact]
        public void FindGameExecutable_RealBinaryInSubfolderLargerThanRootLauncher_ReturnsTheSubfolderBinary()
        {
            /* The Witcher 3 shape, confirmed live 2026-08-08: a small launcher stub at the
               install root (REDprelauncher.exe, ~1.8MB) alongside the real 85MB engine binary
               under bin\x64\. Scanning only the top-level folder picked the stub and
               misdetected the whole game as GraphicsApi.Unknown. */
            WriteFile("REDprelauncher.exe", 1_800_000);
            var expected = WriteFile(Path.Combine("bin", "x64", "witcher3.exe"), 85_000_000);

            var found = PeInspector.FindGameExecutable(installDirectory);

            Assert.Equal(expected, found);
        }

        [Fact]
        public void FindGameExecutable_BundledStreamingAssetsExeLargerThanRealGame_IsNeverPicked()
        {
            /* Vampire Survivors shape, confirmed live 2026-08-10: a bundled Unity relay helper
               under StreamingAssets (replication-server.exe, ~29.5MB) outsized the real
               VampireSurvivors.exe (~672KB), so ReShade deployed next to the helper instead of
               the game and never loaded. */
            WriteFile(Path.Combine("Game_Data", "StreamingAssets", "replication-server.exe"), 29_500_000);
            var expected = WriteFile("VampireSurvivors.exe", 672_000);

            var found = PeInspector.FindGameExecutable(installDirectory);

            Assert.Equal(expected, found);
        }

        [Fact]
        public void FindGameExecutable_LargeRedistributableInstallerPresent_IsNeverPicked()
        {
            WriteFile(Path.Combine("_CommonRedist", "vc_redist.x64.exe"), 50_000_000);
            var expected = WriteFile("game.exe", 10_000_000);

            var found = PeInspector.FindGameExecutable(installDirectory);

            Assert.Equal(expected, found);
        }

        [Fact]
        public void FindGameExecutable_NoExecutables_ReturnsNull()
        {
            var found = PeInspector.FindGameExecutable(installDirectory);

            Assert.Null(found);
        }

        [Fact]
        public void FindGameExecutable_DirectoryDoesNotExist_ReturnsNull()
        {
            var found = PeInspector.FindGameExecutable(Path.Combine(installDirectory, "does-not-exist"));

            Assert.Null(found);
        }
    }
}
