using System.IO;

namespace HdrUpscaleManager.Models.Addons
{
    /* A single row from RenoDX's wiki mod table (https://github.com/clshortfuse/renodx/wiki/Mods),
       observed in RHI's WikiService/GameMod. */
    public class RenoDxWikiMod
    {
        public string Name { get; set; }
        public string Maintainer { get; set; }
        public string SnapshotUrl { get; set; }
        public string SnapshotUrl32 { get; set; }
        public string NexusUrl { get; set; }
        public string DiscordUrl { get; set; }
        public bool IsWorkInProgress { get; set; }
        public string Notes { get; set; }
        public string NameUrl { get; set; }

        public string AddonFileName => SnapshotUrl != null ? Path.GetFileName(SnapshotUrl) : null;
        public string AddonFileName32 => SnapshotUrl32 != null ? Path.GetFileName(SnapshotUrl32) : null;
    }
}
