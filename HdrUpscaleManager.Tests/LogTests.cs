using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HdrUpscaleManager.Services;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class LogTests : IDisposable
    {
        private readonly string logDirectory;

        public LogTests()
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "HdrUpscaleManagerTests", Guid.NewGuid().ToString());
            Log.Initialize(logDirectory);
            Log.MinimumLevel = LogLevel.Debug;
            Log.MaxFileSizeBytes = 5 * 1024 * 1024;
            Log.MaxArchiveCount = 5;
        }

        public void Dispose()
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }

        private string LogFilePath => Path.Combine(logDirectory, "hdr-upscale-manager.log");

        [Fact]
        public void Info_WritesLevelAndMessage()
        {
            Log.Info("hello world");

            var content = File.ReadAllText(LogFilePath);
            Assert.Contains("[INFO]", content);
            Assert.Contains("hello world", content);
        }

        [Fact]
        public void MinimumLevel_FiltersLowerSeverity()
        {
            Log.MinimumLevel = LogLevel.Warn;

            Log.Debug("should not appear");
            Log.Info("should not appear either");
            Log.Warn("this should appear");

            var content = File.ReadAllText(LogFilePath);
            Assert.DoesNotContain("should not appear", content);
            Assert.Contains("this should appear", content);
        }

        [Fact]
        public void Error_WithException_IncludesExceptionDetails()
        {
            var ex = new InvalidOperationException("boom");

            Log.Error(ex, "something failed");

            var content = File.ReadAllText(LogFilePath);
            Assert.Contains("[ERROR]", content);
            Assert.Contains("something failed", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("boom", content);
        }

        [Fact]
        public void ExceedingMaxFileSize_RotatesAndCompresses()
        {
            Log.MaxFileSizeBytes = 1024;

            for (var i = 0; i < 50; i++)
                Log.Info(new string('x', 100));

            var archives = Directory.GetFiles(logDirectory, "*.gz");
            Assert.NotEmpty(archives);

            using (var file = File.OpenRead(archives[0]))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                var decompressed = reader.ReadToEnd();
                Assert.Contains("[INFO]", decompressed);
            }
        }

        [Fact]
        public void ArchiveCount_IsPrunedToMaxArchiveCount()
        {
            Log.MaxFileSizeBytes = 200;
            Log.MaxArchiveCount = 3;

            for (var i = 0; i < 30; i++)
                Log.Info(new string('y', 100));

            var archives = Directory.GetFiles(logDirectory, "*.gz");
            Assert.True(archives.Length <= Log.MaxArchiveCount,
                $"Expected at most {Log.MaxArchiveCount} archives, found {archives.Length}");
        }
    }
}
