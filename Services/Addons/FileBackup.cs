using System;
using System.IO;

namespace HdrUpscaleManager.Services.Addons
{
    /* Shared "back up before overwrite" helper used by every installer that might clobber a
       pre-existing game file (DXVK, OptiScaler) — moves the original to "<name>.original" so
       uninstall can restore it. */
    public static class FileBackup
    {
        public static bool BackupIfNeeded(string targetPath)
        {
            if (!File.Exists(targetPath)) return false;

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
    }
}
