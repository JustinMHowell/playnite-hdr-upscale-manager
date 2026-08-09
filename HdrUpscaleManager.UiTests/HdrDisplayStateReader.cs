using System;
using System.Runtime.InteropServices;

namespace HdrUpscaleManager.UiTests
{
    /* Read-only duplicate of HdrToggleService.TryGetHdrEnabled's DisplayConfig P/Invoke — this
       test project deliberately has no reference back to the addon (see
       HdrUpscaleManager.UiTests.csproj's own comment), so this re-implements just the GET path
       to observe Windows' actual advanced-color state after a real game launch exercises the
       production SET path (inside Playnite's own process, via OnGameStarting/OnGameStopped).
       Never sets state itself — only the real toggle, driven by a real game launch, does that. */
    internal static class HdrDisplayStateReader
    {
        private const int ERROR_SUCCESS = 0;
        private const int QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public int OutputTechnology;
            public int Rotation;
            public int Scaling;
            public DISPLAYCONFIG_RATIONAL RefreshRate;
            public int ScanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO_OPAQUE
        {
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public int Type;
            public uint Size;
            public LUID AdapterId;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
            public uint Value;
            public uint ColorEncoding;
            public uint BitsPerColorChannel;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(int flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            int flags,
            ref uint numPathArrayElements, [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [In, Out] DISPLAYCONFIG_MODE_INFO_OPAQUE[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        public static bool TryGetHdrEnabled(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!TryGetFirstActiveTarget(out var adapterId, out var targetId)) return false;

                var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        Type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        Size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                        AdapterId = adapterId,
                        Id = targetId
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref info) != ERROR_SUCCESS) return false;

                enabled = (info.Value & 0x2) != 0; // bit 1 = advancedColorEnabled
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetFirstActiveTarget(out LUID adapterId, out uint targetId)
        {
            adapterId = default;
            targetId = 0;

            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != ERROR_SUCCESS)
                return false;
            if (pathCount == 0) return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO_OPAQUE[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ERROR_SUCCESS)
                return false;
            if (pathCount == 0) return false;

            adapterId = paths[0].TargetInfo.AdapterId;
            targetId = paths[0].TargetInfo.Id;
            return true;
        }
    }
}
