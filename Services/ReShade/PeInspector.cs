using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HdrUpscaleManager.Services.ReShade
{
    public class PeInspectionResult
    {
        public bool Is64Bit { get; set; }
        public GraphicsApi Api { get; set; }
    }

    /* PE header and import-table parsing observed in RHI's PeHeaderService/GraphicsApiDetector,
       merged into one file read since both need the same DOS/PE/section-table walk. */
    public static class PeInspector
    {
        private const int HeaderBufferSize = 4096;
        private const int ImportReadSize = 8192;

        private static readonly Dictionary<string, GraphicsApi> DllMap =
            new Dictionary<string, GraphicsApi>(StringComparer.OrdinalIgnoreCase)
            {
                ["d3d8.dll"] = GraphicsApi.D3D8,
                ["d3d9.dll"] = GraphicsApi.D3D9,
                ["d3d10.dll"] = GraphicsApi.D3D10,
                ["d3d10_1.dll"] = GraphicsApi.D3D10,
                ["d3d11.dll"] = GraphicsApi.D3D11,
                ["d3d12.dll"] = GraphicsApi.D3D12,
                ["opengl32.dll"] = GraphicsApi.OpenGl,
                ["vulkan-1.dll"] = GraphicsApi.Vulkan,
            };

        private static readonly Dictionary<GraphicsApi, int> Priority = new Dictionary<GraphicsApi, int>
        {
            [GraphicsApi.D3D12] = 7,
            [GraphicsApi.Vulkan] = 6,
            [GraphicsApi.D3D11] = 5,
            [GraphicsApi.D3D10] = 4,
            [GraphicsApi.OpenGl] = 3,
            [GraphicsApi.D3D9] = 2,
            [GraphicsApi.D3D8] = 1,
            [GraphicsApi.Unknown] = 0,
        };

        /* Largest .exe in the top-level install folder — a plain heuristic, but the same one
           RHI itself uses, and DRM/launcher stub executables are reliably smaller than the real game. */
        public static string FindGameExecutable(string installPath)
        {
            var dir = new DirectoryInfo(installPath);
            if (!dir.Exists) return null;

            FileInfo largest = null;
            foreach (var file in dir.GetFiles("*.exe", SearchOption.TopDirectoryOnly))
            {
                if (largest == null || file.Length > largest.Length)
                    largest = file;
            }

            return largest?.FullName;
        }

        public static PeInspectionResult Inspect(string exePath)
        {
            try
            {
                using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var header = new byte[HeaderBufferSize];
                    var headerRead = stream.Read(header, 0, header.Length);

                    if (headerRead < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                        return null;

                    var peOffset = BitConverter.ToInt32(header, 0x3C);
                    if (peOffset < 0 || peOffset + 24 > headerRead)
                        return null;

                    if (header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E' ||
                        header[peOffset + 2] != 0 || header[peOffset + 3] != 0)
                        return null;

                    var coffOffset = peOffset + 4;
                    var machine = BitConverter.ToUInt16(header, coffOffset);
                    var is64Bit = machine == 0x8664;

                    var numberOfSections = BitConverter.ToUInt16(header, coffOffset + 2);
                    var sizeOfOptionalHeader = BitConverter.ToUInt16(header, coffOffset + 16);
                    var optionalHeaderOffset = coffOffset + 20;

                    if (optionalHeaderOffset + sizeOfOptionalHeader > headerRead)
                        return new PeInspectionResult { Is64Bit = is64Bit, Api = GraphicsApi.Unknown };

                    var magic = BitConverter.ToUInt16(header, optionalHeaderOffset);
                    int importDirOffset;
                    if (magic == 0x10B) importDirOffset = optionalHeaderOffset + 104; // PE32
                    else if (magic == 0x20B) importDirOffset = optionalHeaderOffset + 120; // PE32+
                    else return new PeInspectionResult { Is64Bit = is64Bit, Api = GraphicsApi.Unknown };

                    if (importDirOffset + 8 > headerRead)
                        return new PeInspectionResult { Is64Bit = is64Bit, Api = GraphicsApi.Unknown };

                    var importRva = BitConverter.ToUInt32(header, importDirOffset);
                    if (importRva == 0)
                        return new PeInspectionResult { Is64Bit = is64Bit, Api = GraphicsApi.Unknown };

                    var sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
                    var sections = new List<Section>();
                    for (var i = 0; i < numberOfSections; i++)
                    {
                        var secOff = sectionTableOffset + (i * 40);
                        if (secOff + 40 > headerRead) break;
                        sections.Add(new Section
                        {
                            VirtualAddress = BitConverter.ToUInt32(header, secOff + 12),
                            VirtualSize = BitConverter.ToUInt32(header, secOff + 8),
                            RawPointer = BitConverter.ToUInt32(header, secOff + 20)
                        });
                    }

                    var importFileOffset = RvaToFileOffset(sections, importRva);
                    if (importFileOffset < 0)
                        return new PeInspectionResult { Is64Bit = is64Bit, Api = GraphicsApi.Unknown };

                    stream.Seek(importFileOffset, SeekOrigin.Begin);
                    var importBuf = new byte[ImportReadSize];
                    var importRead = stream.Read(importBuf, 0, importBuf.Length);

                    var bestApi = GraphicsApi.Unknown;
                    var bestPriority = 0;
                    var importsDxgi = false;

                    for (var i = 0; ; i++)
                    {
                        var entryOffset = i * 20;
                        if (entryOffset + 20 > importRead) break;

                        var nameRva = BitConverter.ToUInt32(importBuf, entryOffset + 12);
                        if (nameRva == 0) break;

                        var dllName = ReadDllName(stream, sections, nameRva);
                        if (dllName == null) continue;

                        if (dllName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            importsDxgi = true;
                            continue;
                        }

                        GraphicsApi api;
                        if (DllMap.TryGetValue(dllName, out api))
                        {
                            var p = Priority[api];
                            if (p > bestPriority)
                            {
                                bestPriority = p;
                                bestApi = api;
                            }
                        }
                    }

                    // Many DX12 games import only dxgi.dll, never d3d12.dll directly.
                    if (importsDxgi && bestPriority < Priority[GraphicsApi.D3D11])
                        bestApi = GraphicsApi.D3D12;

                    return new PeInspectionResult { Is64Bit = is64Bit, Api = bestApi };
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to inspect PE header for '{exePath}'.");
                return null;
            }
        }

        private static string ReadDllName(FileStream stream, List<Section> sections, uint nameRva)
        {
            var nameFileOffset = RvaToFileOffset(sections, nameRva);
            if (nameFileOffset < 0) return null;

            var savedPos = stream.Position;
            try
            {
                stream.Seek(nameFileOffset, SeekOrigin.Begin);
                var nameBuf = new byte[256];
                var nameRead = stream.Read(nameBuf, 0, nameBuf.Length);
                if (nameRead == 0) return null;

                var end = 0;
                while (end < nameRead && nameBuf[end] != 0) end++;
                return Encoding.ASCII.GetString(nameBuf, 0, end);
            }
            finally
            {
                stream.Seek(savedPos, SeekOrigin.Begin);
            }
        }

        private static long RvaToFileOffset(List<Section> sections, uint rva)
        {
            foreach (var section in sections)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
                    return section.RawPointer + (rva - section.VirtualAddress);
            }
            return -1;
        }

        private class Section
        {
            public uint VirtualAddress { get; set; }
            public uint VirtualSize { get; set; }
            public uint RawPointer { get; set; }
        }
    }
}
