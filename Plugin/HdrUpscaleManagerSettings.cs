using HdrUpscaleManager.Services;
using HdrUpscaleManager.UI;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;

namespace HdrUpscaleManager.Plugin
{
    /* Nothing here is actually persisted or edited — Playnite requires an ISettings-implementing
       type to show a plugin's page under Settings > Add-ons, and the only thing this addon has
       to show there is an About panel (name/version/author/repo, credit to RankFTW/RHI), not
       configurable options. */
    public class HdrUpscaleManagerSettings : HdrUpscaleManager.UI.ObservableObject, ISettings
    {
        public string AddonName => "HDR & Upscale Manager";
        public string VersionText => $"Version {HdrUpscaleManagerPlugin.PluginVersion}";
        public string AuthorName => "Matheus Faustino";
        public string RepoUrl => "https://github.com/matheusfaustino/playnite-hdr-upscale-manager";
        public string RhiRepoUrl => "https://github.com/RankFTW/RHI";

        [DontSerialize]
        public ICommand OpenRepoCommand { get; }
        [DontSerialize]
        public ICommand OpenRhiRepoCommand { get; }

        public HdrUpscaleManagerSettings()
        {
            OpenRepoCommand = new HdrUpscaleManager.UI.RelayCommand(() => OpenUrl(RepoUrl));
            OpenRhiRepoCommand = new HdrUpscaleManager.UI.RelayCommand(() => OpenUrl(RhiRepoUrl));
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to open '{url}'.");
            }
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit() { }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
