using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace HdrUpscaleManager.Plugin
{
    public class HdrUpscaleManagerSettings : ObservableObject, ISettings
    {
        private string greeting = "Hello from HDR & Upscale Manager!";

        public string Greeting { get => greeting; set => SetValue(ref greeting, value); }

        [DontSerialize]
        private readonly HdrUpscaleManagerPlugin plugin;
        [DontSerialize]
        private HdrUpscaleManagerSettings editingClone;

        public HdrUpscaleManagerSettings() { }

        public HdrUpscaleManagerSettings(HdrUpscaleManagerPlugin plugin)
        {
            this.plugin = plugin;

            var saved = plugin.LoadPluginSettings<HdrUpscaleManagerSettings>();
            if (saved != null)
            {
                Greeting = saved.Greeting;
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(this);
        }

        public void CancelEdit()
        {
            if (editingClone == null) return;
            Greeting = editingClone.Greeting;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return errors.Count == 0;
        }
    }
}
