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

        private static readonly Dictionary<string, GraphicsApi> ApiOverrideTokens =
            new Dictionary<string, GraphicsApi>(StringComparer.OrdinalIgnoreCase)
            {
                ["DX8"] = GraphicsApi.D3D8,
                ["DX9"] = GraphicsApi.D3D9,
                ["DX10"] = GraphicsApi.D3D10,
                ["DX11"] = GraphicsApi.D3D11,
                ["DX12"] = GraphicsApi.D3D12,
                ["OGL"] = GraphicsApi.OpenGl,
                ["VLK"] = GraphicsApi.Vulkan,
            };

        /* Manifest override values are RHI's own short tokens, sometimes several comma-
           separated for games that support more than one API (e.g. "DX11, DX12"). Picks
           whichever parses to the highest-priority GraphicsApi via the same ranking real PE
           inspection uses to break ties — for "DX12, VLK" that's D3D12, which also happens to
           be the one with an actual drop-in ReShade proxy, unlike Vulkan. */
        public static GraphicsApi? ParseApiOverride(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            GraphicsApi? best = null;
            var bestPriority = -1;
            foreach (var token in raw.Split(','))
            {
                if (!ApiOverrideTokens.TryGetValue(token.Trim(), out var api)) continue;
                if (Priority[api] > bestPriority)
                {
                    bestPriority = Priority[api];
                    best = api;
                }
            }

            return best;
        }

        /* Largest .exe under the install folder — the same plain heuristic RHI itself uses,
           widened from the top-level folder to every subfolder: confirmed live 2026-08-08 that
           it fails on games whose real engine binary lives in a subfolder while a small
           launcher/bootstrap stub sits at the root (The Witcher 3 — bin\x64\witcher3.exe is
           85MB, but the only top-level .exe is REDprelauncher.exe at 1.8MB; scanning
           TopDirectoryOnly picked the stub and its PE imports never mention a graphics DLL at
           all, misreporting the whole game as GraphicsApi.Unknown). DRM/launcher stubs are
           still reliably smaller than the real game once every subfolder is in play — the
           exclusions below only guard against installer/uninstaller/crash-handler executables
           that could otherwise rival or beat an indie game's exe in size despite never being
           "the game" under any definition. Confirmed live 2026-08-10 on Vampire Survivors: a
           bundled Unity relay helper, VampireSurvivors_Data\StreamingAssets\replication-server.exe
           (29.5MB), beat the real VampireSurvivors.exe (672KB) outright, so ReShade deployed
           into StreamingAssets and never loaded. Unity's own StreamingAssets folder is raw
           asset payload by convention — it never contains the player's own launchable exe —
           so it's excluded by path rather than guessing at name fragments. */
        private static readonly string[] ExcludedExecutableNameFragments =
        {
            "unins", "redist", "setup", "prereq", "vc_redist", "dxsetup", "crashreport", "crashhandler",
        };

        private static readonly string[] ExcludedExecutablePathFragments =
        {
            "streamingassets",
        };

        public static string FindGameExecutable(string installPath)
        {
            var dir = new DirectoryInfo(installPath);
            if (!dir.Exists) return null;

            FileInfo largest = null;
            foreach (var file in dir.GetFiles("*.exe", SearchOption.AllDirectories))
            {
                var nameLower = file.Name.ToLowerInvariant();
                if (Array.Exists(ExcludedExecutableNameFragments, fragment => nameLower.Contains(fragment)))
                    continue;

                var pathLower = file.FullName.ToLowerInvariant();
                if (Array.Exists(ExcludedExecutablePathFragments, fragment => pathLower.Contains(fragment)))
                    continue;

                if (largest == null || file.Length > largest.Length)
                    largest = file;
            }

            return largest?.FullName;
        }

        /* User-facing reason FindGameExecutable came back null. An unreachable folder usually
           means the drive holding the game isn't mounted, not that the game has no exe —
           confirmed live 2026-09-30 on RuneScape: Dragonwilds, an Xbox app game whose
           C:\Program Files\WindowsApps\... folder is a junction to X:\WindowsApps on an external
           drive that doesn't remount itself after a reboot. */
        public static string DescribeMissingExecutable(string installPath)
        {
            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
                return $"Can't reach the install folder '{installPath}'. If the game is on an external or secondary " +
                    "drive (Xbox app games can live on a separate drive behind this path), make sure that drive is " +
                    "connected and mounted, then try again.";

            return $"No executable found in the install folder '{installPath}'.";
        }

        /* Every installer (OptiScaler/ReShade/DXVK/RenoDX/Luma) used to deploy straight into
           installPath itself — wrong whenever the real, loadable exe FindGameExecutable finds
           sits in a subfolder (e.g. Unreal Engine's Binaries\Win64\), since Windows only checks
           a DLL's own directory (ahead of System32) for proxy-DLL hijacking, never an unrelated
           parent folder. Confirmed live 2026-08-09 on Deep Rock Galactic: a proxy dxgi.dll dropped
           at the install root next to the launcher stub FSD.exe never loaded — the same file
           dropped next to the real FSD-Win64-Shipping.exe under FSD\Binaries\Win64\ did. Falls
           back to installPath itself if no exe is found, matching the pre-fix behavior for that
           case (nothing to resolve against, same as before). */
        public static string ResolveDeployDirectory(string installPath)
        {
            var exePath = FindGameExecutable(installPath);
            return exePath != null ? Path.GetDirectoryName(exePath) : installPath;
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
            catch (UnauthorizedAccessException)
            {
                /* Xbox app / Game Pass games: Windows denies reading the game's own .exe outright
                   (execute-only — confirmed live 2026-09-30 on Blue Prince, and 2026-09-23 on Dune:
                   Awakening, where every ReShade install failed on it), from both the WindowsApps
                   path Playnite reports and the C:\XboxGames\...\Content folder it's a junction to.
                   Everything else in the folder stays readable and writable, so only this header
                   read needs a way around it. */
                var fromManifest = InspectFromPackageManifest(exePath);
                if (fromManifest != null)
                {
                    // Inspect runs many times per install/status refresh — note it once per exe.
                    bool firstTime;
                    lock (LoggedManifestFallbacks) firstTime = LoggedManifestFallbacks.Add(exePath);
                    if (firstTime)
                        Log.Info($"'{exePath}' isn't readable (Xbox app package) — took bitness from its appxmanifest.xml; graphics API unknown.");
                    return fromManifest;
                }

                Log.Warn($"Failed to inspect PE header for '{exePath}': access denied, and no appxmanifest.xml found to fall back on.");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to inspect PE header for '{exePath}'.");
                return null;
            }
        }

        private const int ManifestSearchDepth = 8;

        private static readonly HashSet<string> LoggedManifestFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /* Walks up from the exe to the package root (a UE game's exe sits several folders deep,
           e.g. DuneSandbox\Binaries\WinGDK\) looking for the appxmanifest.xml every Xbox app
           package carries, and reads bitness from its Identity ProcessorArchitecture. The API
           can't be derived from the manifest, so it's always Unknown — callers already treat
           that as "ask the manifest override / PCGamingWiki, else default". */
        internal static PeInspectionResult InspectFromPackageManifest(string exePath)
        {
            var dir = Path.GetDirectoryName(exePath);
            for (var depth = 0; dir != null && depth < ManifestSearchDepth; depth++, dir = Path.GetDirectoryName(dir))
            {
                var manifestPath = Path.Combine(dir, "appxmanifest.xml");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        File.ReadAllText(manifestPath),
                        @"<Identity\b[^>]*\bProcessorArchitecture\s*=\s*""([^""]+)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!match.Success) return null;

                    var arch = match.Groups[1].Value;
                    return new PeInspectionResult
                    {
                        Is64Bit = !arch.Equals("x86", StringComparison.OrdinalIgnoreCase),
                        Api = GraphicsApi.Unknown
                    };
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, $"Failed to read '{manifestPath}'.");
                    return null;
                }
            }

            return null;
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
