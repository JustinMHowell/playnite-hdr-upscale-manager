using System.IO;
using System.Linq;
using SharpCompress.Archives.SevenZip;

namespace HdrUpscaleManager.Services.Addons
{
    /* Shared 7z extraction for addons that ship real 7-Zip archives (DXVK+Lilium, OptiScaler) —
       unlike ReShade's installer, which is a disguised zip (see ReShadeArchiveExtractor.cs),
       these are genuine LZMA/7z archives, which needs a real decoder rather than a signature scan. */
    public static class SevenZipExtractor
    {
        public static void ExtractAll(byte[] archiveBytes, string destinationDirectory)
        {
            using (var stream = new MemoryStream(archiveBytes))
            using (var archive = SevenZipArchive.Open(stream))
            {
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    var destPath = Path.Combine(destinationDirectory, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? destinationDirectory);

                    using (var entryStream = entry.OpenEntryStream())
                    using (var fileStream = File.Create(destPath))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }
        }

        public static string FindFile(string rootDirectory, string fileName, string preferredPathSegment = null)
        {
            var matches = Directory.EnumerateFiles(rootDirectory, fileName, SearchOption.AllDirectories).ToList();
            if (matches.Count == 0) return null;
            if (preferredPathSegment == null) return matches[0];

            return matches.FirstOrDefault(m => m.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(seg => string.Equals(seg, preferredPathSegment, System.StringComparison.OrdinalIgnoreCase)))
                ?? matches[0];
        }
    }
}
