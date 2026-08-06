using System;
using System.Runtime.InteropServices;

namespace HdrUpscaleManager.Services
{
    /* Toggles Windows' "Use HDR" display setting via the CCD (Connecting and Configuring
       Displays) DisplayConfig API — there's no public high-level .NET/WinRT API for actually
       setting this (only reading it), so this goes through the same low-level surface community
       tools (e.g. HDR Tray) use. Modeled after lscholte/PlayniteEnableHdr's approach of toggling
       HDR around a play session (see PLAN.md §0), reimplemented here since that project's own
       toggle code isn't reusable as a library.

       UNVERIFIED: no HDR-capable display was available during development to test this against.
       Every P/Invoke call is wrapped so a failure here logs and returns false rather than
       throwing — this must never be able to block a game launch. Validate manually before
       relying on it, and only enable per-game via the opt-in menu item (never automatic/global),
       per this project's "no silent actions on the user's behalf" principle (PLAN.md §5). */
    public static class HdrToggleService
    {
        private const int ERROR_SUCCESS = 0;
        private const int QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
        private const int DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

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

        /* Opaque — DISPLAYCONFIG_MODE_INFO is a 64-byte tagged union we never read from; it only
           needs to be sized correctly so QueryDisplayConfig can fill the array. */
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

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
            public uint Value;
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

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE requestPacket);

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
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to read the current HDR state.");
                return false;
            }
        }

        public static bool TrySetHdrEnabled(bool enabled)
        {
            try
            {
                if (!TryGetFirstActiveTarget(out var adapterId, out var targetId)) return false;

                var request = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
                {
                    Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        Type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                        Size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(),
                        AdapterId = adapterId,
                        Id = targetId
                    },
                    Value = enabled ? 1u : 0u
                };

                if (DisplayConfigSetDeviceInfo(ref request) != ERROR_SUCCESS)
                {
                    Log.Warn($"DisplayConfigSetDeviceInfo returned a failure code while setting HDR to {enabled}.");
                    return false;
                }

                Log.Info($"Set display HDR state to {enabled}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to set HDR state to {enabled}.");
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
