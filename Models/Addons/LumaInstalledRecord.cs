using System;
using System.Collections.Generic;

namespace HdrUpscaleManager.Models.Addons
{
    /* Tracks a Luma install per game, modeled on RHI's LumaInstalledRecord. Luma can deploy
       multiple files from a zip (unlike RenoDX's single .addon64), so uninstall/update tracking
       is keyed off the file list rather than a single install path. */
    public class LumaInstalledRecord
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string InstallPath { get; set; }
        public List<string> InstalledFiles { get; set; } = new List<string>();
        public string DownloadUrl { get; set; }
        /* Parsed from the Luma-Framework GitHub release tag (e.g. "latest-428" -> 428).
           0 for locally-installed/unknown builds — CheckForUpdateAsync can't compare those. */
        public int InstalledBuildNumber { get; set; }
        public DateTimeOffset InstalledAtUtc { get; set; }
    }
}
