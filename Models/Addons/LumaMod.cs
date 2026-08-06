namespace HdrUpscaleManager.Models.Addons
{
    /* A single row from Luma-Framework's wiki mod table
       (https://github.com/Filoppi/Luma-Framework/wiki), observed in RHI's LumaService. */
    public class LumaMod
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string DownloadUrl { get; set; }
        /* "✅" (complete) or "🚧" (work in progress). */
        public string Status { get; set; }
        public string SpecialNotes { get; set; }
    }
}
