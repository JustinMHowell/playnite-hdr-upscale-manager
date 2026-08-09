using System;

namespace HdrUpscaleManager.UiTests
{
    /* Real-launch log-verification tests (PLAN.md §9) target specific games from one person's
       actual library — a GameId or install path hardcoded here would be both meaningless on
       anyone else's machine and unnecessary personal-library detail to publish in source at all.
       Same idea as PlayniteFixture's HUM_UITEST_PLAYNITE_EXE override: real values live in
       environment variables (and are recorded, for local reference only, in PLAN.md's own
       scenario table — that file is deliberately never committed). */
    internal static class LocalTestGameConfig
    {
        public readonly struct GameConfig
        {
            public GameConfig(string gameId, string installDirectory)
            {
                GameId = gameId;
                InstallDirectory = installDirectory;
            }

            public string GameId { get; }
            public string InstallDirectory { get; }
        }

        /// <summary>
        /// Reads HUM_UITEST_{gameKey}_GAME_ID / _INSTALL_DIR. See PLAN.md §9's scenario table
        /// for the real values to set for each gameKey (e.g. "VAMPIRESURVIVORS"). No exe name is
        /// needed — GameLaunchAndLogCapture detects whichever process(es) actually run under the
        /// install directory rather than requiring one guessed in advance (see its own doc
        /// comment for why: a launcher stub and the real engine process can be two different
        /// exes, confirmed live on Deep Rock Galactic 2026-08-09).
        /// </summary>
        public static GameConfig Resolve(string gameKey)
        {
            return new GameConfig(
                Require(gameKey, "GAME_ID"),
                Require(gameKey, "INSTALL_DIR"));
        }

        private static string Require(string gameKey, string suffix)
        {
            var varName = $"HUM_UITEST_{gameKey}_{suffix}";
            var value = Environment.GetEnvironmentVariable(varName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{varName}' isn't set. These are local/library-specific " +
                    "integration tests — see PLAN.md §9's scenario table for this game's real GameId/" +
                    $"install path, then set HUM_UITEST_{gameKey}_GAME_ID and HUM_UITEST_{gameKey}_INSTALL_DIR " +
                    "before running.");
            }

            return value;
        }
    }
}
