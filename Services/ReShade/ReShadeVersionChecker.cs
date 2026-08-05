using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HdrUpscaleManager.Services.ReShade
{
    public class ReShadeVersionInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
    }

    /* Latest-version lookup observed in RHI's ReShadeUpdateService: reshade.me has no API for
       this, so the version comes from regex-matching the download link off the homepage HTML. */
    public static class ReShadeVersionChecker
    {
        private const string ReShadeMeUrl = "https://reshade.me";

        private static readonly Regex DownloadLinkRegex = new Regex(
            @"/downloads/ReShade_Setup_([\d.]+)_Addon\.exe",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HttpClient Http = new HttpClient();

        public static async Task<ReShadeVersionInfo> CheckLatestAsync()
        {
            try
            {
                var html = await Http.GetStringAsync(ReShadeMeUrl).ConfigureAwait(false);
                var match = DownloadLinkRegex.Match(html);
                if (!match.Success)
                {
                    Log.Warn("Could not find the ReShade addon download link on reshade.me.");
                    return null;
                }

                return new ReShadeVersionInfo
                {
                    Version = match.Groups[1].Value,
                    Url = ReShadeMeUrl + match.Value
                };
            }
            catch (System.Exception ex)
            {
                Log.Warn(ex, "Failed to check reshade.me for the latest ReShade version.");
                return null;
            }
        }
    }
}
