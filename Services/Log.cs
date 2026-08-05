using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace HdrUpscaleManager.Services
{
    /* Leveled file logger with size-based rotation and gzip-compressed archives. */
    public static class Log
    {
        public static long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
        public static int MaxArchiveCount { get; set; } = 5;

        private static readonly object WriteLock = new object();
        private static string logFilePath =
            Path.Combine(Path.GetTempPath(), "HdrUpscaleManager", "hdr-upscale-manager.log");

        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public static void Initialize(string logDirectory)
        {
            Directory.CreateDirectory(logDirectory);
            logFilePath = Path.Combine(logDirectory, "hdr-upscale-manager.log");
        }

        public static void Debug(string message) => Write(LogLevel.Debug, message, null);
        public static void Info(string message) => Write(LogLevel.Info, message, null);
        public static void Warn(string message) => Write(LogLevel.Warn, message, null);
        public static void Warn(Exception ex, string message) => Write(LogLevel.Warn, message, ex);
        public static void Error(string message) => Write(LogLevel.Error, message, null);
        public static void Error(Exception ex, string message) => Write(LogLevel.Error, message, ex);

        private static void Write(LogLevel level, string message, Exception ex)
        {
            if (level < MinimumLevel) return;

            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}";
            if (ex != null) line += Environment.NewLine + ex;

            lock (WriteLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                    RotateIfNeeded();
                    File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // A logging failure must never throw back into the caller.
                }
            }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(logFilePath)) return;
            if (new FileInfo(logFilePath).Length < MaxFileSizeBytes) return;

            /* Millisecond precision (plus a guid fallback on collision) — a burst of rotations
               within the same second would otherwise overwrite each other's archive. */
            var archivePath = logFilePath + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".gz";
            if (File.Exists(archivePath))
                archivePath = logFilePath + "." + Guid.NewGuid().ToString("N") + ".gz";
            using (var source = File.OpenRead(logFilePath))
            using (var archive = File.Create(archivePath))
            using (var gzip = new GZipStream(archive, CompressionLevel.Optimal))
            {
                source.CopyTo(gzip);
            }
            File.Delete(logFilePath);

            PruneOldArchives();
        }

        private static void PruneOldArchives()
        {
            var directory = Path.GetDirectoryName(logFilePath);
            var archives = Directory.GetFiles(directory, Path.GetFileName(logFilePath) + ".*.gz")
                .OrderByDescending(f => f)
                .ToList();

            foreach (var stale in archives.Skip(MaxArchiveCount))
                File.Delete(stale);
        }
    }
}
