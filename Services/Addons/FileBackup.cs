using System;
using System.Diagnostics;
using System.IO;

namespace HdrUpscaleManager.Services.Addons
{
    /* Shared "back up before overwrite" helper used by every installer that might clobber a
       pre-existing game file (DXVK, OptiScaler) — moves the original to "<name>.original" so
       uninstall can restore it. */
    public static class FileBackup
    {
        /* isOwnFile identifies a file this addon itself put there on an earlier install (a
           reinstall, or leftovers a previous uninstall missed). Those are overwritten, never
           backed up: confirmed live 2026-09-30 on Atomic Heart that backing up OptiScaler's own
           earlier copy as ".original" made every later uninstall "restore" OptiScaler right back,
           so it could never actually be removed. */
        public static bool BackupIfNeeded(string targetPath, Func<string, bool> isOwnFile = null)
        {
            if (!File.Exists(targetPath)) return false;
            if (isOwnFile != null && isOwnFile(targetPath)) return false;

            var backupPath = targetPath + ".original";
            if (File.Exists(backupPath)) return false;

            try
            {
                File.Move(targetPath, backupPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to back up existing file at '{targetPath}' before install.");
                return false;
            }
        }

        public static bool FilesEqual(string pathA, string pathB)
        {
            try
            {
                var a = new FileInfo(pathA);
                var b = new FileInfo(pathB);
                if (!a.Exists || !b.Exists || a.Length != b.Length) return false;

                using (var sa = a.OpenRead())
                using (var sb = b.OpenRead())
                {
                    var bufA = new byte[81920];
                    var bufB = new byte[81920];
                    int readA;
                    while ((readA = sa.Read(bufA, 0, bufA.Length)) > 0)
                    {
                        var readB = sb.Read(bufB, 0, readA);
                        if (readB != readA) return false;
                        for (var i = 0; i < readA; i++)
                            if (bufA[i] != bufB[i]) return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not compare '{pathA}' with '{pathB}'.");
                return false;
            }
        }

        /* The ProductName from a DLL's version resource, or "" when it has none (or can't be read). */
        public static string ProductNameOf(string path)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(path).ProductName?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not read version info for '{path}'.");
                return "";
            }
        }
    }
}
