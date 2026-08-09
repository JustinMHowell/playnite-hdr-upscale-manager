using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Scenario 4 of PLAN.md §9 — real-launch log verification for Luma. Also the run meant to
       settle PLAN.md §9's own open question: "does Luma log anything, and where" — every Luma
       mod inspected so far ships its own self-hosted dxgi.dll/.addon rather than depending on a
       separately-installed ReShade (see the Hard Dependencies table in PLAN.md §6.5), but nobody
       had confirmed whether that self-hosted proxy produces a log file at all until this test
       actually launches a game with it installed.

       Deliberately doesn't assert a specific log line/filename yet, matching the calibration-
       first principle of every other scenario test in this pass — this run's job is to capture
       whatever the real directory listing/log content looks like after a real launch, and print
       it, so a follow-up can pick real confirmed assertions instead of guessing.

       Game: Vanquish — GAMES-TO-TEST.md's own Luma-matched pick, confirmed installed. Real exe
       (Vanquish.exe) sits at the install root. */
    [Collection(PlayniteUiCollection.Name)]
    public class LumaMatchedLaunchLogUiTests
    {
        private const string GameName = "Vanquish";
        private const string GameConfigKey = "VANQUISH";
        private const string LumaRecordStoreFileName = "luma_installs.json";

        private readonly ITestOutputHelper output;

        public LumaMatchedLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallLumaMatchedThenLaunch_RealGame_CapturesWhateverItLogs()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var dir = game.InstallDirectory;
            GuardNoPreExistingInstall(dir);

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
            LumaRecordDto record = null;

            try
            {
                const int maxAttempts = 10;
                var appeared = false;
                for (var attempt = 1; attempt <= maxAttempts && !appeared; attempt++)
                {
                    ManagerSectionNavigation.ClickSectionButton(managerWindow, "Luma", "Install", output);
                    appeared = Retry.WhileFalse(
                        () => (record = FindLumaRecordByGameName(GameName)) != null,
                        timeout: TimeSpan.FromSeconds(6),
                        interval: TimeSpan.FromMilliseconds(500)).Result;
                }

                if (!appeared)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallLumaMatchedThenLaunch_RealGame_CapturesWhateverItLogs), output);
                    output.WriteLine(AutomationTreeDumper.Dump(managerWindow));
                }
                Assert.True(appeared, $"Luma install didn't complete for '{GameName}' within the retry window.");

                output.WriteLine($"Luma installed under '{record.InstallPath}' — files: {string.Join(", ", record.InstalledFiles)}");
                foreach (var relative in record.InstalledFiles)
                {
                    var fullPath = Path.Combine(record.InstallPath, relative);
                    Assert.True(File.Exists(fullPath), $"Luma record lists '{relative}' but it isn't on disk at '{fullPath}'.");
                }

                var beforeLaunch = new HashSet<string>(Directory.EnumerateFiles(record.InstallPath), StringComparer.OrdinalIgnoreCase);

                managerWindow.Close();
                managerWindow = null;

                // Watches ReShade.log speculatively — Luma's own GitHub repo describes itself as
                // "based on the ReShade Addon system", so its self-hosted proxy might still emit
                // ReShade-formatted output despite not depending on a separate ReShade install.
                var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                    game.GameId, record.InstallPath, new[] { "ReShade.log" }, output);

                var reShadeLogPath = Path.Combine(record.InstallPath, "ReShade.log");
                var content = logContents[reShadeLogPath];
                output.WriteLine($"ReShade.log exists: {content != null}");
                if (content != null)
                {
                    output.WriteLine("--- ReShade.log content (first 4000 chars) ---");
                    output.WriteLine(content.Length > 4000 ? content.Substring(0, 4000) : content);
                    output.WriteLine("--- end ReShade.log content ---");
                }

                // Whatever new files showed up after launch, regardless of name — the actual
                // discovery step PLAN.md §9 asked for.
                var afterLaunch = Directory.EnumerateFiles(record.InstallPath).ToList();
                var newFiles = afterLaunch.Where(p => !beforeLaunch.Contains(p)).ToList();
                output.WriteLine($"New files in '{record.InstallPath}' after launch ({newFiles.Count}): {string.Join(", ", newFiles.Select(Path.GetFileName))}");
                foreach (var newFile in newFiles.Where(p => p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    output.WriteLine($"--- {newFile} (first 2000 chars) ---");
                    var text = TryReadAllText(newFile);
                    output.WriteLine(text == null ? "(couldn't read)" : (text.Length > 2000 ? text.Substring(0, 2000) : text));
                    output.WriteLine("--- end ---");
                }
            }
            finally
            {
                try { managerWindow?.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                CleanUp(dir, record, testStartedUtc);
            }
        }

        private void GuardNoPreExistingInstall(string dir)
        {
            var record = FindLumaRecordByGameName(GameName);
            if (record != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a tracked Luma install (InstallPath: {record.InstallPath}) before " +
                    "this test started. This test installs and cleans up Luma itself — uninstall it manually first.");
            }
        }

        private void CleanUp(string dir, LumaRecordDto record, DateTime testStartedUtc)
        {
            if (record == null) return;

            foreach (var relative in record.InstalledFiles)
            {
                CleanUpFreshlyCreatedFile(Path.Combine(record.InstallPath, relative), testStartedUtc);
            }

            // Luma's own record store entry — mirrors what ManagerViewModel's UninstallLumaAsync
            // does (LumaRecords.Remove), done directly here since there's no need to reopen the
            // manager window just to click a button for a plain JSON file edit.
            try
            {
                var path = PluginPaths.RecordStorePath(LumaRecordStoreFileName);
                if (File.Exists(path))
                {
                    var all = JsonConvert.DeserializeObject<Dictionary<string, LumaRecordDto>>(File.ReadAllText(path)) ?? new Dictionary<string, LumaRecordDto>();
                    var key = all.FirstOrDefault(kvp => string.Equals(kvp.Value.GameName, GameName, StringComparison.OrdinalIgnoreCase)).Key;
                    if (key != null)
                    {
                        all.Remove(key);
                        File.WriteAllText(path, JsonConvert.SerializeObject(all));
                        output.WriteLine($"Removed Luma record for '{GameName}' from '{path}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to remove Luma record for '{GameName}': {ex.Message}");
            }
        }

        private static LumaRecordDto FindLumaRecordByGameName(string gameName)
        {
            var path = PluginPaths.RecordStorePath(LumaRecordStoreFileName);
            if (!File.Exists(path)) return null;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, LumaRecordDto>>(File.ReadAllText(path));
                return all?.Values.FirstOrDefault(r => string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string TryReadAllText(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private void CleanUpFreshlyCreatedFile(string path, DateTime testStartedUtc)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (File.GetCreationTimeUtc(path) >= testStartedUtc.AddSeconds(-2))
            {
                File.Delete(path);
                output.WriteLine($"Cleaned up '{path}' (created by this test run).");
            }
            else
            {
                output.WriteLine($"Leaving '{path}' in place — it predates this test run.");
            }
        }

        private class LumaRecordDto
        {
            public string GameName { get; set; }
            public string InstallPath { get; set; }
            public List<string> InstalledFiles { get; set; } = new List<string>();
        }
    }
}
