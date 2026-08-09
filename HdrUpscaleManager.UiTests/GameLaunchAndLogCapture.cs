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
       well under the timeout instead of always waiting it out. */
    internal static class GameLaunchAndLogCapture
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(1);

        public static Dictionary<string, string> LaunchWaitForLogsThenKill(
            string gameId,
            string gameInstallDirectory,
            string gameExePath,
            IReadOnlyList<string> logFileNames,
            ITestOutputHelper output,
            TimeSpan? timeout = null)
        {
            var logPaths = logFileNames.Select(name => Path.Combine(gameInstallDirectory, name)).ToArray();
            foreach (var path in logPaths)
            {
                output.WriteLine($"Watching log file: {path}");
            }

            output.WriteLine($"Launching game via playnite://playnite/start/{gameId}");
            Process.Start(new ProcessStartInfo($"playnite://playnite/start/{gameId}") { UseShellExecute = true });

            WaitForLogsToSettle(logPaths, timeout ?? DefaultTimeout, output);

            KillGameProcess(gameExePath, output);

            var contents = new Dictionary<string, string>();
            foreach (var path in logPaths)
            {
                var exists = File.Exists(path);
                output.WriteLine($"Log file location: {path} (exists: {exists})");
                contents[path] = exists ? ReadAllTextEvenIfLocked(path) : null;
            }

            return contents;
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

        /* Matches by the exact exe path (from PeInspector.FindGameExecutable) rather than "any
           process whose working directory is under the install folder" — a launcher/updater
           process can briefly touch files there too, and an exact executable match is simpler
           and safer than guessing at that boundary. */
        private static void KillGameProcess(string gameExePath, ITestOutputHelper output)
        {
            var killedAny = false;

            foreach (var process in Process.GetProcesses())
            {
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

                if (!string.Equals(mainModulePath, gameExePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                output.WriteLine($"Killing game process '{process.ProcessName}' (pid {process.Id}) at '{mainModulePath}'.");
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
                output.WriteLine($"No running process matched '{gameExePath}' to kill — it may not have launched, " +
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
