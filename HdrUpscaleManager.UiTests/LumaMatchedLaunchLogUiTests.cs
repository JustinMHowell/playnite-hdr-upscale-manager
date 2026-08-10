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
    /* Scenario 4 of PLAN.md §9 — real-launch log verification for Luma. Also the run that settled
       PLAN.md §9's own open question: "does Luma log anything, and where" — every Luma mod
       inspected so far ships its own self-hosted dxgi.dll/.addon rather than depending on a
       separately-installed ReShade (see the Hard Dependencies table in PLAN.md §6.5). Confirmed
       live 2026-08-10: yes, a real ReShade.log (v6.7.3.2, 32-bit — Luma's own bundled fork), with
       `Registered add-on "Luma"` as the confirmed line proving it actually loaded, not just that
       files landed on disk.

       Vanquish itself exits ~1.3s after the swap chain is created regardless of Luma — an
       already-documented, unrelated finding (PLAN.md's dgVoodoo2/Vanquish entry: likely a Steam
       DRM/ownership check failing because Playnite launches the exe directly). That happens well
       after Luma's own registration line, so it doesn't affect this assertion.

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
        public void InstallLumaMatchedThenLaunch_RealGame_ReShadeLogShowsLumaRegistered()
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
                    ManagerSectionNavigation.ClickButton(managerWindow, "LumaInstall", output);
                    appeared = Retry.WhileFalse(
                        () => (record = FindLumaRecordByGameName(GameName)) != null,
                        timeout: TimeSpan.FromSeconds(6),
                        interval: TimeSpan.FromMilliseconds(500)).Result;
                }

                if (!appeared)
                {
                    WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallLumaMatchedThenLaunch_RealGame_ReShadeLogShowsLumaRegistered), output);
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
                    output.WriteLine("--- ReShade.log content (first 6000 chars) ---");
                    output.WriteLine(content.Length > 6000 ? content.Substring(0, 6000) : content);
                    output.WriteLine("--- end ReShade.log content ---");
                }

                Assert.False(string.IsNullOrWhiteSpace(content), "ReShade.log is missing or empty — Luma's self-hosted proxy may not have loaded.");
                Assert.Contains("Registered add-on \"Luma\"", content);

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
                CleanUp(fixture, managerWindow, dir, record, testStartedUtc);
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

        private void CleanUp(PlayniteFixture fixture, Window managerWindow, string dir, LumaRecordDto record, DateTime testStartedUtc)
        {
            if (record == null)
            {
                try { managerWindow?.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
                return;
            }

            try
            {
                if (managerWindow == null)
                {
                    // Reuses the SAME PlayniteFixture the test opened at the top rather than
                    // constructing a new one — PlayniteFixture's constructor kills and relaunches
                    // Playnite unconditionally (see its own doc comment), and a second `using`
                    // scoped to this if-block would dispose that fixture (and shut Playnite down)
                    // the instant the block ends, right after handing back a managerWindow tied to
                    // the now-disposed Automation session. Confirmed live 2026-08-09: this produced
                    // a stale/unsupported automation tree and "Couldn't find the 'Uninstall' button"
                    // on every run. GameLaunchAndLogCapture only ever touches the GAME process, so
                    // the outer fixture's Playnite instance is still running here.
                    managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
                }

                // Uses the real "Uninstall" button (LumaService.Uninstall) rather than hand-
                // deleting files — that production path already correctly removes now-empty
                // subfolders (confirmed live 2026-08-08, per its own doc comment), which an
                // earlier version of this test's cleanup didn't and left an empty Luma\ tree
                // behind on Vanquish.
                const int maxAttempts = 5;
                var recordGone = false;
                for (var attempt = 1; attempt <= maxAttempts && !recordGone; attempt++)
                {
                    ManagerSectionNavigation.ClickButton(managerWindow, "LumaUninstall", output);
                    recordGone = Retry.WhileFalse(
                        () => FindLumaRecordByGameName(GameName) == null,
                        timeout: TimeSpan.FromSeconds(3),
                        interval: TimeSpan.FromMilliseconds(250)).Result;
                }
                if (!recordGone) output.WriteLine($"Luma record for '{GameName}' still present after {maxAttempts} Uninstall clicks.");

                try { managerWindow.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — '{GameName}' may need manual cleanup: {ex.Message}");
                throw;
            }

            // Luma's own runtime log (ReShade.log — see the class doc comment) isn't a file
            // LumaService tracks/removes, so it needs its own cleanup line.
            CleanUpFreshlyCreatedFile(Path.Combine(record.InstallPath, "ReShade.log"), testStartedUtc);
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
