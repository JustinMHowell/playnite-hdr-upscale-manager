using System;
using System.IO;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    /* Xbox app packages deny reading the game's .exe, so bitness comes from appxmanifest.xml. */
    public class PeInspectorPackageManifestTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public PeInspectorPackageManifestTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        private void WriteManifest(string arch) =>
            File.WriteAllText(Path.Combine(directory, "appxmanifest.xml"),
                $@"<Package><Identity Name=""RawFury.BluePrince"" Publisher=""CN=X"" Version=""1.1.12.0"" ProcessorArchitecture=""{arch}"" /></Package>");

        [Theory]
        [InlineData("x64", true)]
        [InlineData("x86", false)]
        public void InspectFromPackageManifest_ReadsBitnessFromIdentity(string arch, bool expected64)
        {
            WriteManifest(arch);

            var result = PeInspector.InspectFromPackageManifest(Path.Combine(directory, "Game.exe"));

            Assert.NotNull(result);
            Assert.Equal(expected64, result.Is64Bit);
            Assert.Equal(GraphicsApi.Unknown, result.Api);
        }

        [Fact]
        public void InspectFromPackageManifest_ExeInNestedFolder_WalksUpToPackageRoot()
        {
            // Unreal GDK layout, e.g. DuneSandbox\Binaries\WinGDK\DuneSandbox-WinGDK-Shipping.exe.
            WriteManifest("x64");
            var nested = Path.Combine(directory, "Game", "Binaries", "WinGDK");
            Directory.CreateDirectory(nested);

            var result = PeInspector.InspectFromPackageManifest(Path.Combine(nested, "Game-WinGDK-Shipping.exe"));

            Assert.NotNull(result);
            Assert.True(result.Is64Bit);
        }

        [Fact]
        public void InspectFromPackageManifest_NoManifest_ReturnsNull()
        {
            Assert.Null(PeInspector.InspectFromPackageManifest(Path.Combine(directory, "Game.exe")));
        }
    }
}
