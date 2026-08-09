using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Shared by the real-launch log-verification tests (PLAN.md §9) — launches a real installed
       game through Playnite's own URI command, waits for the log file(s) a mod writes on load to
       show up and settle, then kills the game so the test doesn't leave it running.

       "Settle" is the detection signal, not a fixed sleep: a log file that exists but is still
       growing means the game/mod is still writing startup output, so this polls file size until
       it's unchanged for a full second before treating it as loaded — letting most runs finish
       well under the timeout instead of always waiting it out.

       Does NOT take a specific exe path to kill — confirmed live 2026-08-09 on Deep Rock
       Galactic that the top-level exe (what PeInspector.FindGameExecutable would treat as "the"
       game exe for a game with no obviously-bigger nested binary) can be a small launcher stub
       that spawns a separate, much larger real engine process (FSD.exe, ~27MB resident, spawning
       FSD-Win64-Shipping.exe, ~900MB resident) — killing only the guessed exe leaves the real one
       running. Snapshotting PIDs before launch and killing every NEW process rooted under the
       install directory afterward catches both, without needing to guess which one is "real". */
    internal static class GameLaunchAndLogCapture
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(1);

        public static Dictionary<string, string> LaunchWaitForLogsThenKill(
            string gameId,
            string gameInstallDirectory,
            IReadOnlyList<string> logFileNames,
            ITestOutputHelper output,
            TimeSpan? timeout = null)
        {
            var logPaths = logFileNames.Select(name => Path.Combine(gameInstallDirectory, name)).ToArray();
            foreach (var path in logPaths)
            {
                output.WriteLine($"Watching log file: {path}");
            }

            var pidsBeforeLaunch = new HashSet<int>(Process.GetProcesses().Select(p => p.Id));

            output.WriteLine($"Launching game via playnite://playnite/start/{gameId}");
            Process.Start(new ProcessStartInfo($"playnite://playnite/start/{gameId}") { UseShellExecute = true });

            WaitForLogsToSettle(logPaths, timeout ?? DefaultTimeout, output);

            KillNewGameProcesses(gameInstallDirectory, pidsBeforeLaunch, output);

            var contents = new Dictionary<string, string>();
            foreach (var path in logPaths)
            {
                var exists = File.Exists(path);
                output.WriteLine($"Log file location: {path} (exists: {exists})");
                contents[path] = exists ? ReadAllTextEvenIfLocked(path) : null;
            }

            return contents;
        }

        /* For cases with no log file to watch (e.g. the OS HDR toggle — it writes nothing to
           disk at all) — waits a fixed duration instead of polling for a settle signal, then
           kills the same way LaunchWaitForLogsThenKill does. */
        public static void LaunchWaitFixedThenKill(string gameId, string gameInstallDirectory, TimeSpan waitDuration, ITestOutputHelper output)
        {
            var pidsBeforeLaunch = new HashSet<int>(Process.GetProcesses().Select(p => p.Id));

            output.WriteLine($"Launching game via playnite://playnite/start/{gameId}");
            Process.Start(new ProcessStartInfo($"playnite://playnite/start/{gameId}") { UseShellExecute = true });

            output.WriteLine($"Waiting a fixed {waitDuration.TotalSeconds}s for the game to start (no log file to watch for this case).");
            Thread.Sleep(waitDuration);

            KillNewGameProcesses(gameInstallDirectory, pidsBeforeLaunch, output);
        }

        private static void WaitForLogsToSettle(string[] logPaths, TimeSpan timeout, ITestOutputHelper output)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            var lastSize = new Dictionary<string, long>();
            var lastChangedUtc = new Dictionary<string, DateTime>();

            while (DateTime.UtcNow < deadline)
            {
                var allSettled = true;

                foreach (var path in logPaths)
                {
                    if (!File.Exists(path))
                    {
                        allSettled = false;
                        continue;
                    }

                    long size;
                    try
                    {
                        size = new FileInfo(path).Length;
                    }
                    catch (IOException)
                    {
                        // Locked mid-write by the game process — treat as "still changing".
                        allSettled = false;
                        continue;
                    }

                    if (!lastSize.TryGetValue(path, out var previousSize) || previousSize != size)
                    {
                        lastSize[path] = size;
                        lastChangedUtc[path] = DateTime.UtcNow;
                        allSettled = false;
                        continue;
                    }

                    if (DateTime.UtcNow - lastChangedUtc[path] < StableFor)
                    {
                        allSettled = false;
                    }
                }

                if (allSettled)
                {
                    output.WriteLine("All watched log files stopped growing — treating the game as loaded.");
                    return;
                }

                Thread.Sleep(PollInterval);
            }

            output.WriteLine($"Timed out after {timeout.TotalSeconds}s waiting for log files to settle — " +
                "killing the game anyway and reading whatever is on disk now.");
        }

        /* Kills every process that (a) didn't exist before the launch and (b) runs an exe
           somewhere under gameInstallDirectory — covers a launcher stub and whatever real engine
           process it spawns without needing to know in advance which one is "real" (see class
           doc comment). PIDs get reused by the OS over time, so the before/after PID snapshot is
           inherently a best-effort signal, not a guarantee — acceptable here since this only
           needs to avoid killing something that was already running, not perfect identity. */
        private static void KillNewGameProcesses(string gameInstallDirectory, HashSet<int> pidsBeforeLaunch, ITestOutputHelper output)
        {
            var normalizedDir = Path.GetFullPath(gameInstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var killedAny = false;

            foreach (var process in Process.GetProcesses())
            {
                if (pidsBeforeLaunch.Contains(process.Id)) continue;

                string mainModulePath;
                try
                {
                    mainModulePath = process.MainModule?.FileName;
                }
                catch
                {
                    // Access denied (system/elevated processes) or the process exited already.
                    continue;
                }

                if (string.IsNullOrEmpty(mainModulePath)) continue;

                string fullModulePath;
                try
                {
                    fullModulePath = Path.GetFullPath(mainModulePath);
                }
                catch
                {
                    continue;
                }

                if (!fullModulePath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase)) continue;

                output.WriteLine($"Killing new game process '{process.ProcessName}' (pid {process.Id}) at '{mainModulePath}'.");
                try
                {
                    process.Kill();
                    process.WaitForExit(10_000);
                    killedAny = true;
                }
                catch (Exception ex)
                {
                    output.WriteLine($"Failed to kill process {process.Id}: {ex.Message}");
                }
            }

            if (!killedAny)
            {
                output.WriteLine($"No new process rooted under '{gameInstallDirectory}' found to kill — it may not have launched, " +
                    "or already exited on its own.");
            }
        }

        /* The game process (or the mod writing to it) may still hold the file open when the
           "settled" check above fires — FileShare.ReadWrite lets this read alongside that. */
        private static string ReadAllTextEvenIfLocked(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
