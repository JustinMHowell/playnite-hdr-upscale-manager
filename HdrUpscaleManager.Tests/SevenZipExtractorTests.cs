using System;
using System.IO;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class SevenZipExtractorTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public SevenZipExtractorTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        [Fact]
        public void FindFile_NoMatch_ReturnsNull()
        {
            Assert.Null(SevenZipExtractor.FindFile(root, "OptiScaler.dll"));
        }

        [Fact]
        public void FindFile_SingleMatch_ReturnsIt()
        {
            var path = Path.Combine(root, "OptiScaler.dll");
            File.WriteAllBytes(path, new byte[] { 0 });

            Assert.Equal(path, SevenZipExtractor.FindFile(root, "OptiScaler.dll"));
        }

        [Fact]
        public void FindFile_PreferredPathSegmentPresent_PicksThatOneOverOthers()
        {
            var x64Dir = Directory.CreateDirectory(Path.Combine(root, "normal", "x64")).FullName;
            var x32Dir = Directory.CreateDirectory(Path.Combine(root, "normal", "x32")).FullName;
            File.WriteAllBytes(Path.Combine(x64Dir, "d3d9.dll"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(x32Dir, "d3d9.dll"), new byte[] { 2 });

            var found = SevenZipExtractor.FindFile(root, "d3d9.dll", preferredPathSegment: "x32");

            Assert.Equal(Path.Combine(x32Dir, "d3d9.dll"), found);
        }

        [Fact]
        public void FindFile_PreferredSegmentAbsent_FallsBackToFirstMatch()
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "somewhere")).FullName;
            var path = Path.Combine(dir, "d3d9.dll");
            File.WriteAllBytes(path, new byte[] { 1 });

            var found = SevenZipExtractor.FindFile(root, "d3d9.dll", preferredPathSegment: "x64");

            Assert.Equal(path, found);
        }
    }
}
