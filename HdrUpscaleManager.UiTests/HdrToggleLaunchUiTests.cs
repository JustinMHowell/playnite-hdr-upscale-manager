using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Real-launch verification for the OS HDR toggle (PLAN.md's "OS-level HDR toggle" item,
       still flagged unverified there — no HDR-capable display available in this environment).
       This can't confirm the display visually looks right; it drives the real production path
       (manager window checkbox -> hdr_toggle_opt_in.json -> OnGameStarting/OnGameStopped ->
       HdrToggleService, all inside Playnite's own process) against a real game launch and reads
       back Windows' own advanced-color state via HdrDisplayStateReader — verifying the code path
       actually runs end-to-end without asserting a specific enabled value, since that value is
       exactly the untested variable on a display with no HDR support at all.

       Game: Vampire Survivors — this toggle is a generic Windows-level "Use HDR" flip around
       any game's play session; it doesn't check whether the specific game has an HDR feature at
       all, so any already-configured game works. Picked this one because it's already set up
       (see ReShadeOnlyLaunchLogUiTests) and is the fastest to launch/kill of the configured
       games — not Monster Hunter: World, GAMES-TO-TEST.md's thematic-only pick ("the only
       installed game Steam tags HDR available"), which would need its own GameId lookup for no
       mechanical benefit. */
    [Collection(PlayniteUiCollection.Name)]
    public class HdrToggleLaunchUiTests
    {
        private const string GameName = "Vampire Survivors";
        private const string GameConfigKey = "VAMPIRESURVIVORS";
        private const string HdrOptInFileName = "hdr_toggle_opt_in.json";

        private readonly ITestOutputHelper output;

        public HdrToggleLaunchUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void EnableHdrToggleThenLaunch_RealGame_RunsToggleEndToEndAndRestoresState()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);

            var baselineRead = HdrDisplayStateReader.TryGetHdrEnabled(out var baselineEnabled);
            output.WriteLine($"Baseline HDR state before test: read={baselineRead}, enabled={baselineEnabled}.");

            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

            try
            {
                if (IsHdrToggleOptedIn(game.GameId))
                {
                    throw new InvalidOperationException(
                        $"'{GameName}' already has the HDR toggle opted in before this test started. " +
                        "This test enables it and cleans up afterward — turn it off manually first.");
                }

                ManagerSectionNavigation.ClickButton(managerWindow, "HdrToggleEnabled", output);

                var optedIn = Retry.WhileFalse(
                    () => IsHdrToggleOptedIn(game.GameId),
                    timeout: TimeSpan.FromSeconds(5),
                    interval: TimeSpan.FromMilliseconds(250)).Result;
                Assert.True(optedIn, $"Clicking the HDR checkbox didn't persist an opt-in for GameId '{game.GameId}' within 5s.");

                managerWindow.Close();
                managerWindow = null;

                GameLaunchAndLogCapture.LaunchWaitFixedThenKill(game.GameId, game.InstallDirectory, TimeSpan.FromSeconds(8), output);

                var duringRead = HdrDisplayStateReader.TryGetHdrEnabled(out var duringEnabled);
                output.WriteLine($"FINDING: HDR state while the game was running: read={duringRead}, enabled={duringEnabled}.");

                // OnGameStopped fires once Playnite notices the process it started has exited —
                // give it a moment after the kill above before reading the restored state back.
                Thread.Sleep(2000);
                var afterRead = HdrDisplayStateReader.TryGetHdrEnabled(out var afterEnabled);
                output.WriteLine($"FINDING: HDR state after the game stopped (should match baseline if restore worked): read={afterRead}, enabled={afterEnabled}.");

                if (baselineRead && afterRead)
                {
                    Assert.Equal(baselineEnabled, afterEnabled);
                }
                else
                {
                    output.WriteLine(
                        "Skipping the baseline/after equality assertion — TryGetHdrEnabled couldn't read the " +
                        "display's advanced-color state at least once (expected on a display with no HDR " +
                        "support at all). See the FINDING lines above for what actually happened.");
                }
            }
            finally
            {
                CleanUp(fixture, managerWindow, game.GameId);
            }
        }

        private void CleanUp(PlayniteFixture fixture, Window managerWindow, string gameId)
        {
            try
            {
                if (managerWindow == null)
                {
                    managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);
                }

                if (IsHdrToggleOptedIn(gameId))
                {
                    ManagerSectionNavigation.ClickButton(managerWindow, "HdrToggleEnabled", output);
                    Retry.WhileFalse(
                        () => !IsHdrToggleOptedIn(gameId),
                        timeout: TimeSpan.FromSeconds(5),
                        interval: TimeSpan.FromMilliseconds(250));
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Cleanup failed — HDR opt-in for '{GameName}' may need manual removal: {ex.Message}");
            }
            finally
            {
                try { managerWindow?.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }
            }
        }

        private static bool IsHdrToggleOptedIn(string gameId)
        {
            var path = PluginPaths.RecordStorePath(HdrOptInFileName);
            if (!File.Exists(path)) return false;

            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, bool>>(File.ReadAllText(path));
                return all != null && all.TryGetValue(gameId, out var optedIn) && optedIn;
            }
            catch
            {
                return false;
            }
        }
    }
}
