using System;
using System.Collections.Generic;

namespace HdrUpscaleManager.Models.Addons
{
    /* Tracks an OptiScaler install per game. Directory entries in InstalledFiles carry a
       trailing path separator so Uninstall knows to recurse rather than delete a single file. */
    public class OptiScalerInstalledRecord
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string InstallPath { get; set; }
        public string DllName { get; set; }
        public List<string> InstalledFiles { get; set; } = new List<string>();
        public bool DlssInputs { get; set; }
        public bool OptiPatcherInstalled { get; set; }
        public bool DlssDllInstalled { get; set; }
        public string Version { get; set; }
        public DateTimeOffset InstalledAtUtc { get; set; }
    }
}
