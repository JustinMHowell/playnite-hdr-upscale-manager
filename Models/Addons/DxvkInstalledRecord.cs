using System;
using System.Collections.Generic;

namespace HdrUpscaleManager.Models.Addons
{
    /* Tracks a DXVK+Lilium HDR install per game, modeled on RHI's DxvkInstalledRecord — richer
       than a single-file record since uninstall must restore any backed-up original DLLs too. */
    public class DxvkInstalledRecord
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string InstallPath { get; set; }
        public List<string> InstalledDlls { get; set; } = new List<string>();
        /* Subset of InstalledDlls that had a pre-existing game file backed up as "<name>.original". */
        public List<string> BackedUpFiles { get; set; } = new List<string>();
        public bool DeployedConf { get; set; }
        public string Version { get; set; }
        public DateTimeOffset InstalledAtUtc { get; set; }
    }
}
