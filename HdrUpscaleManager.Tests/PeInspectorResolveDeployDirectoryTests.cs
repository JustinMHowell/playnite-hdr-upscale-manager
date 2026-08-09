using System;
using System.IO;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    /* Regression coverage for the deployment-location bug confirmed live on Deep Rock Galactic
       2026-08-09: a proxy DLL dropped at the Playnite-reported install root never loaded because
       the real, running exe lived in a subfolder (a launcher stub occupied the root instead).
       OptiScalerService/ReShadeInstallService/DxvkService/AddonInstallService/LumaService all
       route their file deployment through this resolver now instead of assuming installPath IS
       the deploy directory. */
    public class PeInspectorResolveDeployDirectoryTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public PeInspectorResolveDeployDirectoryTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Fact]
        public void ResolveDeployDirectory_ExeAtRoot_ReturnsRootItself()
        {
            File.WriteAllBytes(Path.Combine(directory, "game.exe"), new byte[100]);

            var resolved = PeInspector.ResolveDeployDirectory(directory);

            Assert.Equal(directory, resolved);
        }

        [Fact]
        public void ResolveDeployDirectory_RealExeNestedUnderLauncherStub_ReturnsNestedDirectory()
        {
            // Mirrors Deep Rock Galactic's real shape: a small root-level launcher stub plus the
            // actual engine binary nested several folders deep, much larger.
            File.WriteAllBytes(Path.Combine(directory, "Launcher.exe"), new byte[1_000]);
            var nestedDir = Path.Combine(directory, "FSD", "Binaries", "Win64");
            Directory.CreateDirectory(nestedDir);
            File.WriteAllBytes(Path.Combine(nestedDir, "FSD-Win64-Shipping.exe"), new byte[1_000_000]);

            var resolved = PeInspector.ResolveDeployDirectory(directory);

            Assert.Equal(nestedDir, resolved);
        }

        [Fact]
        public void ResolveDeployDirectory_AlreadyResolvedDirectory_IsIdempotent()
        {
            // Every caller in this codebase is safe to pass either the raw install root or an
            // already-resolved deploy directory (e.g. cross-service calls between
            // OptiScalerService and ReShadeInstallService) — re-resolving a directory that
            // already contains the real exe must return that same directory, not something else.
            var nestedDir = Path.Combine(directory, "bin", "x64");
            Directory.CreateDirectory(nestedDir);
            File.WriteAllBytes(Path.Combine(nestedDir, "game.exe"), new byte[1_000]);

            var resolved = PeInspector.ResolveDeployDirectory(nestedDir);

            Assert.Equal(nestedDir, resolved);
        }

        [Fact]
        public void ResolveDeployDirectory_NoExeFound_FallsBackToInstallPathItself()
        {
            var resolved = PeInspector.ResolveDeployDirectory(directory);

            Assert.Equal(directory, resolved);
        }
    }
}
