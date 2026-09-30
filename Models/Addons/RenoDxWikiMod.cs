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

        /* The wiki heading the row's table sits under — "Legend" for the main per-game list,
           "Unreal Engine" / "Unreal Engine Extended" / "Unity Engine" for the engine
           compatibility lists, and so on. Null for rows cached before this was tracked. */
        public string Section { get; set; }

        /* Rows from the engine compatibility lists describe how a game behaves under the generic
           engine addon — they're never a per-game download of their own. */
        public bool IsEngineCompatEntry =>
            Section != null &&
            (Section.IndexOf("unreal engine", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             Section.IndexOf("unity", System.StringComparison.OrdinalIgnoreCase) >= 0);

        public string AddonFileName => SnapshotUrl != null ? Path.GetFileName(SnapshotUrl) : null;
        public string AddonFileName32 => SnapshotUrl32 != null ? Path.GetFileName(SnapshotUrl32) : null;
    }
}
