using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Xunit;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Scenario 1 of PLAN.md §9 — real-launch log verification, calibration pass. Installs
       ReShade only (no RenoDX/Luma/OptiScaler layered on), launches the real game through
       Playnite, and captures ReShade.log to confirm it actually loaded at runtime rather than
       just checking file presence like OptiScalerInstallUiTests/RenoDxInstallUiTests do.

       Asserts on "crosire's ReShade" (the initializing-version banner line) rather than the
       full version string — confirmed live 2026-08-10 against a real ReShade.log (v6.8.0.2155),
       and the version number will drift with future ReShade releases while the author name won't.

       Game: Vampire Survivors — small and quick to launch/kill, good baseline pick. NOT Elden
       Ring — excluded from all testing per GAMES-TO-TEST.md (mid-playthrough). Real GameId/
       install path/exe name are local-library-specific, so they're not hardcoded here — see
       LocalTestGameConfig and PLAN.md §9's scenario table (set HUM_UITEST_VAMPIRESURVIVORS_*
       before running this). */
    [Collection(PlayniteUiCollection.Name)]
    public class ReShadeOnlyLaunchLogUiTests
    {
        private const string GameName = "Vampire Survivors";
        private const string GameConfigKey = "VAMPIRESURVIVORS";

        // Same candidate list RenoDxInstallUiTests uses — the drop-in proxy names ReShade's own
        // installer picks from depending on the game's detected graphics API.
        private static readonly string[] ReShadeProxyDllCandidates = { "d3d9.dll", "dxgi.dll", "opengl32.dll" };

        private readonly ITestOutputHelper output;

        public ReShadeOnlyLaunchLogUiTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InstallReShadeThenLaunch_RealGame_ReShadeLogAppearsAndLoads()
        {
            var game = LocalTestGameConfig.Resolve(GameConfigKey);
            var gameInstallDirectory = game.InstallDirectory;

            var preExistingProxyPath = FindExistingReShadeProxy(gameInstallDirectory);
            if (preExistingProxyPath != null)
            {
                throw new InvalidOperationException(
                    $"'{GameName}' already has a ReShade proxy DLL at '{preExistingProxyPath}' before this " +
                    "test started. This test installs (and cleans up) ReShade itself, so it refuses to run " +
                    "against a game that already has a real install it didn't create — remove it manually first.");
            }

            var testStartedUtc = DateTime.UtcNow;
            using var fixture = new PlayniteFixture();
            fixture.GetMainWindow();
            var managerWindow = ManagerWindowNavigation.Open(fixture, GameName, output);

            string reShadeProxyPath = null;
            try
            {
                ManagerSectionNavigation.ClickButton(managerWindow, "ReShadeInstall", output);

                reShadeProxyPath = WaitForReShadeInstall(managerWindow, gameInstallDirectory);
                output.WriteLine($"ReShade installed as '{reShadeProxyPath}'.");

                managerWindow.Close();
                managerWindow = null;

                var logContents = GameLaunchAndLogCapture.LaunchWaitForLogsThenKill(
                    game.GameId, gameInstallDirectory, new[] { "ReShade.log" }, output);

                var reShadeLogPath = Path.Combine(gameInstallDirectory, "ReShade.log");
                var content = logContents[reShadeLogPath];

                Assert.True(File.Exists(reShadeLogPath), $"Expected 'ReShade.log' to exist at '{reShadeLogPath}' after launching the game.");
                Assert.Contains("crosire's ReShade", content);

                output.WriteLine("--- ReShade.log content (first 4000 chars) ---");
                output.WriteLine(content.Length > 4000 ? content.Substring(0, 4000) : content);
                output.WriteLine("--- end ReShade.log content ---");
            }
            finally
            {
                try { managerWindow?.Close(); } catch (Exception ex) { output.WriteLine($"managerWindow.Close() failed during cleanup: {ex.Message}"); }

                CleanUpFreshlyCreatedFile(reShadeProxyPath, testStartedUtc, output);
                CleanUpFreshlyCreatedFile(Path.Combine(gameInstallDirectory, "ReShade.log"), testStartedUtc, output);
                CleanUpFreshlyCreatedFile(Path.Combine(gameInstallDirectory, "ReShade.ini"), testStartedUtc, output);
            }
        }

        private string WaitForReShadeInstall(Window managerWindow, string gameInstallDirectory)
        {
            string found = null;
            var appeared = Retry.WhileFalse(
                () => (found = FindExistingReShadeProxy(gameInstallDirectory)) != null,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500)).Result;

            if (!appeared)
            {
                WindowScreenshot.CaptureOnTimeout(managerWindow, nameof(InstallReShadeThenLaunch_RealGame_ReShadeLogAppearsAndLoads), output);
                throw new InvalidOperationException(
                    $"None of {string.Join(", ", ReShadeProxyDllCandidates)} appeared in '{gameInstallDirectory}' " +
                    "within 30s of clicking 'Install latest' — the install may have failed. Check the manager " +
                    "window's status text or the plugin log for the real reason.");
            }

            return found;
        }

        private static string FindExistingReShadeProxy(string gameInstallDirectory) =>
            ReShadeProxyDllCandidates
                .Select(name => Path.Combine(gameInstallDirectory, name))
                .FirstOrDefault(File.Exists);

        private static void CleanUpFreshlyCreatedFile(string path, DateTime testStartedUtc, ITestOutputHelper output)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            // Only remove what this run actually created — a pre-existing file (e.g. a
            // ReShade.ini a user already had) isn't this test's to delete. Same CreationTimeUtc
            // reasoning RenoDxInstallUiTests already uses for its own ReShade cleanup.
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
    }
}
