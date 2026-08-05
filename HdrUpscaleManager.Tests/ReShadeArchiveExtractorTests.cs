using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class ReShadeArchiveExtractorTests : IDisposable
    {
        private readonly string installerPath;
        private readonly string destinationDirectory;

        public ReShadeArchiveExtractorTests()
        {
            var root = Path.Combine(Path.GetTempPath(), "ReShadeArchiveExtractorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            installerPath = Path.Combine(root, "fake_installer.exe");
            destinationDirectory = Path.Combine(root, "extracted");
        }

        public void Dispose()
        {
            var root = Path.GetDirectoryName(installerPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        [Fact]
        public void ExtractFiles_ZipAppendedAfterExeStub_SkipsRedHerringAndExtracts()
        {
            var junkWithRedHerring = BuildJunkPrefixWithRedHerringSignature();
            var zipBytes = BuildZip(("ReShade64.dll", Encoding.ASCII.GetBytes("fake-64-bit-dll-content")));
            File.WriteAllBytes(installerPath, Combine(junkWithRedHerring, zipBytes));

            var result = ReShadeArchiveExtractor.ExtractFiles(installerPath, destinationDirectory, "ReShade64.dll");

            Assert.True(result);
            var extractedPath = Path.Combine(destinationDirectory, "ReShade64.dll");
            Assert.Equal("fake-64-bit-dll-content", File.ReadAllText(extractedPath));
        }

        [Fact]
        public void ExtractFiles_RequiresAllNamedFilesPresent()
        {
            var zipBytes = BuildZip(("ReShade64.dll", Encoding.ASCII.GetBytes("only-64-bit")));
            File.WriteAllBytes(installerPath, zipBytes);

            var result = ReShadeArchiveExtractor.ExtractFiles(installerPath, destinationDirectory, "ReShade64.dll", "ReShade32.dll");

            Assert.False(result);
            Assert.False(File.Exists(Path.Combine(destinationDirectory, "ReShade64.dll")));
        }

        [Fact]
        public void ExtractFiles_NoEmbeddedZip_ReturnsFalse()
        {
            File.WriteAllBytes(installerPath, Encoding.ASCII.GetBytes("just some garbage, not an installer at all"));

            var result = ReShadeArchiveExtractor.ExtractFiles(installerPath, destinationDirectory, "ReShade64.dll");

            Assert.False(result);
        }

        [Fact]
        public void ExtractFiles_MissingInstallerFile_ReturnsFalse()
        {
            var result = ReShadeArchiveExtractor.ExtractFiles(
                Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".exe"),
                destinationDirectory,
                "ReShade64.dll");

            Assert.False(result);
        }

        // A red herring: the local-file-header signature appears here but isn't followed by
        // a real zip structure, matching how the real ReShade installer has an earlier
        // coincidental match before the actual embedded archive.
        private static byte[] BuildJunkPrefixWithRedHerringSignature()
        {
            var junk = Encoding.ASCII.GetBytes("MZ-fake-exe-stub-");
            var redHerring = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            var moreJunk = Encoding.ASCII.GetBytes("-not-actually-a-zip-here-just-a-coincidental-byte-match-");
            return Combine(junk, redHerring, moreJunk);
        }

        private static byte[] BuildZip(params (string name, byte[] content)[] entries)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var (name, content) in entries)
                    {
                        var entry = archive.CreateEntry(name);
                        using (var entryStream = entry.Open())
                            entryStream.Write(content, 0, content.Length);
                    }
                }

                return stream.ToArray();
            }
        }

        private static byte[] Combine(params byte[][] parts)
        {
            var totalLength = 0;
            foreach (var part in parts) totalLength += part.Length;

            var result = new byte[totalLength];
            var offset = 0;
            foreach (var part in parts)
            {
                part.CopyTo(result, offset);
                offset += part.Length;
            }

            return result;
        }
    }
}
