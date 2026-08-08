using System;
using System.IO;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Regression test for PlayniteFixture.GetMainWindow's "Startup Error" dialog handling —
       added after a real run (2026-08-08) sat stuck behind this dialog for the full length of
       ManifestAutoDownloadUiTests' file-wait, because that test only called GetMainWindow()
       AFTER its own wait had already timed out (fixed separately). Rather than relying on a
       previous test run happening to leave Playnite flagged as "closed unexpectedly" — which
       is what actually triggered that failure — this test creates Playnite's own crash-
       detection flag file directly, so the dialog is guaranteed to appear on the very next
       launch.

       Mechanism (read straight from reference/Playnite/source, not guessed): Playnite checks
       for an empty file at "safestart.flag" under its config root on every startup
       (PlayniteApplication.cs, guarded by `#if !DEBUG` — a real installed Playnite is always a
       Release build, so this always applies here). If present, it shows the "closed
       unexpectedly... start in safe mode?" dialog; the file is only deleted once startup
       finishes cleanly (DesktopApplication.cs, after the main window opens and the startup
       library update finishes) — so its continued absence afterward is itself a signal that
       recovery actually completed, not just that some window happened to appear. */
    [Collection(PlayniteUiCollection.Name)]
    public class StartupErrorDialogUiTests
    {
        private static readonly string FlagFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Playnite", "safestart.flag");

        private readonly ITestOutputHelper output;

        public StartupErrorDialogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void GetMainWindow_WhenStartupErrorDialogAppears_DismissesItAndStartsNormally()
        {
            var logPath = Path.Combine(PluginPaths.DataDir, "logs", "hdr-upscale-manager.log");
            var logLinesBefore = File.Exists(logPath) ? File.ReadAllLines(logPath).Length : 0;

            using (var fixture = PlayniteFixture.LaunchFresh(beforeLaunch: () =>
            {
                // Only its existence matters to Playnite's own check — contents are irrelevant.
                File.WriteAllBytes(FlagFilePath, Array.Empty<byte>());
            }))
            {
                // Throws with a clear message if the dialog is never dismissed or the real
                // window never stabilizes — that alone covers most of this test's intent.
                var mainWindow = fixture.GetMainWindow();
                Assert.Equal("Playnite", mainWindow.Name);

                // DismissStartupErrorIfPresent always answers "No" (see its own comment on why
                // "Yes" would defeat the point of every other test here) — "No" means a normal
                // startup, not safe mode. Confirm 3rd-party add-ons, ours included, actually
                // loaded rather than being silently disabled for this session: the plugin logs
                // a line on every load, so a fresh one appearing is direct evidence safe mode
                // wasn't entered.
                var pluginLoaded = Retry.WhileFalse(
                    () => File.Exists(logPath) && File.ReadAllLines(logPath).Length > logLinesBefore,
                    timeout: TimeSpan.FromSeconds(20),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                if (!pluginLoaded)
                {
                    output.WriteLine($"No new line appended to '{logPath}' after dismissing the dialog.");
                }

                Assert.True(pluginLoaded,
                    "The plugin's own log didn't gain a new line after the Startup Error dialog " +
                    "was dismissed — if 'Yes' had been clicked instead of 'No', Playnite would " +
                    "have started in safe mode with 3rd-party add-ons (including this one) disabled.");

                var flagCleared = Retry.WhileFalse(
                    () => !File.Exists(FlagFilePath),
                    timeout: TimeSpan.FromSeconds(20),
                    interval: TimeSpan.FromMilliseconds(250)).Result;

                Assert.True(flagCleared,
                    $"Playnite's own '{FlagFilePath}' was still present after startup — it only " +
                    "deletes this once it considers itself cleanly started.");
            }
        }
    }
}
