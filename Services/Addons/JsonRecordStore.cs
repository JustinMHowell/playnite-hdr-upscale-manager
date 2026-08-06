using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace HdrUpscaleManager.Services.Addons
{
    /* Disk-backed dictionary of records, keyed by Playnite's stable game Id — shared shape used
       by every addon that needs an install record (RenoDX, Luma, DXVK) for update-detection and
       uninstall, modeled on RHI's per-addon JSON stores (renodx_installed.json, luma_installed.json,
       dxvk_installed.json). */
    public class JsonRecordStore<T>
    {
        private readonly string filePath;

        public JsonRecordStore(string directory, string fileName)
        {
            Directory.CreateDirectory(directory);
            filePath = Path.Combine(directory, fileName);
        }

        public T Get(string key)
        {
            return Load().TryGetValue(key, out var record) ? record : default;
        }

        public void Set(string key, T record)
        {
            var all = Load();
            all[key] = record;
            Save(all);
        }

        public void Remove(string key)
        {
            var all = Load();
            if (all.Remove(key)) Save(all);
        }

        private Dictionary<string, T> Load()
        {
            if (!File.Exists(filePath)) return new Dictionary<string, T>();

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, T>>(File.ReadAllText(filePath))
                    ?? new Dictionary<string, T>();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to read record store '{filePath}'.");
                return new Dictionary<string, T>();
            }
        }

        private void Save(Dictionary<string, T> all)
        {
            try
            {
                File.WriteAllText(filePath, JsonConvert.SerializeObject(all));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to write record store '{filePath}'.");
            }
        }
    }
}
