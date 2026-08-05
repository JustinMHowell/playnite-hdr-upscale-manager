using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HdrUpscaleManager.Services.ReShade
{
    /* ReShade's installer stores its payload as a zip archive appended after the PE image
       (7z reports it as an "overlay" at a PE-structure-computed offset). System.IO.Compression
       doesn't apply that offset correction when opening the raw exe, so this finds the real
       zip start itself by trying each local-file-header signature occurrence in turn until
       one actually parses and contains what we're looking for. */
    public static class ReShadeArchiveExtractor
    {
        private static readonly byte[] LocalFileHeaderSignature = { 0x50, 0x4B, 0x03, 0x04 };

        public static bool ExtractFiles(string installerPath, string destinationDirectory, params string[] fileNames)
        {
            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(installerPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to read installer '{installerPath}'.");
                return false;
            }

            foreach (var offset in FindCandidateOffsets(fileBytes))
            {
                if (TryExtractAt(fileBytes, offset, destinationDirectory, fileNames))
                    return true;
            }

            Log.Error($"Could not locate an embedded archive containing {string.Join(", ", fileNames)} in '{installerPath}'.");
            return false;
        }

        private static bool TryExtractAt(byte[] fileBytes, int offset, string destinationDirectory, string[] fileNames)
        {
            try
            {
                using (var stream = new MemoryStream(fileBytes, offset, fileBytes.Length - offset, writable: false))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entries = fileNames.Select(name => archive.GetEntry(name)).ToList();
                    if (entries.Any(e => e == null))
                        return false;

                    Directory.CreateDirectory(destinationDirectory);
                    foreach (var entry in entries)
                        entry.ExtractToFile(Path.Combine(destinationDirectory, entry.Name), overwrite: true);

                    return true;
                }
            }
            catch (InvalidDataException)
            {
                // Not a real zip start — just a coincidental 4-byte match, try the next one.
                return false;
            }
        }

        private static IEnumerable<int> FindCandidateOffsets(byte[] bytes)
        {
            for (var i = 0; i <= bytes.Length - LocalFileHeaderSignature.Length; i++)
            {
                if (bytes[i] == LocalFileHeaderSignature[0] && bytes[i + 1] == LocalFileHeaderSignature[1] &&
                    bytes[i + 2] == LocalFileHeaderSignature[2] && bytes[i + 3] == LocalFileHeaderSignature[3])
                {
                    yield return i;
                }
            }
        }
    }
}
