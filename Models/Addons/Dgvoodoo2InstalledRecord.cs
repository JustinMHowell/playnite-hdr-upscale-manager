using System;
using System.Collections.Generic;

namespace HdrUpscaleManager.Models.Addons
{
    /* Tracks a dgVoodoo2 install per game — a Luma prerequisite for pre-DX11 games, not a
       component installed on its own initiative. Same shape as DxvkInstalledRecord. */
    public class Dgvoodoo2InstalledRecord
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string InstallPath { get; set; }
        public List<string> InstalledFiles { get; set; } = new List<string>();
        /* Subset of InstalledFiles that had a pre-existing game file backed up as "<name>.original". */
        public List<string> BackedUpFiles { get; set; } = new List<string>();
        public string Version { get; set; }
        public DateTimeOffset InstalledAtUtc { get; set; }
    }
}
