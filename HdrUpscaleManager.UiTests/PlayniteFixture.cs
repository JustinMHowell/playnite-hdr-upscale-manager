using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Newtonsoft.Json.Linq;

namespace HdrUpscaleManager.UiTests
{
    /* Shared across every test in a class via IClassFixture<T> so Playnite is only
       launched once per test run, not once per [Fact].

       IMPORTANT — this always kills any running Playnite and relaunches it. That's not
       optional: Playnite's AccessibilityInterface setting (see PlayniteConfigAccessibility.cs)
       is marked [RequiresRestart] in Playnite's own source, so patching config.json only takes
       effect on a fresh launch. Without this, every window's automation tree is a hardcoded-
       empty stub — confirmed by reading Playnite's WindowBase.cs directly, not guessed. If you
       have Playnite open with unsaved state, close/save it yourself before running these tests. */
    public class PlayniteFixture : IDisposable
    {
        public UIA3Automation Automation { get; }
        public Application App { get; }

        private readonly JToken previousAccessibilityValue;
        private readonly string exePath;

        public PlayniteFixture() : this(beforeLaunch: null)
        {
        }

        private PlayniteFixture(Action beforeLaunch)
        {
            Automation = new UIA3Automation();

            exePath = ResolveExePath();
            KillIfRunning(exePath);

            // Runs after any already-running Playnite is fully closed and before the new
            // instance launches — the one ordering a test needs to observe "what happens on
            // a truly fresh launch" (e.g. deleting manifest.json to watch OnApplicationStarted
            // recreate it) without a race against either the old instance or the new one.
            beforeLaunch?.Invoke();

            previousAccessibilityValue = PlayniteConfigAccessibility.ForceAlwaysOn();

            // --nolibupdate skips the startup library-update scan (ProcessStartupLibUpdate in
            // Playnite's own MainViewModelBase.cs) — confirmed from source this only gates that
            // scan, not plugin loading or OnApplicationStarted (InitializeView fires that
            // unconditionally, before the update step even runs), so tests relying on
            // OnApplicationStarted are unaffected. What it DOES fix: DesktopApplication.cs only
            // deletes the safe-startup flag file (see StartupErrorDialogUiTests) after this
            // scan finishes — on a large real library that scan can run well past a test's
            // patience, which is exactly what made that test flaky before this flag was added.
            //
            // Deliberately NOT passing --hidesplashscreen: tried it alongside --nolibupdate and
            // GetMainWindow started timing out completely (confirmed live 2026-08-08) — with no
            // splash window, there's apparently nothing for FlaUI's Process.MainWindowHandle-
            // based main-window discovery to latch onto early, including while the "Startup
            // Error" MessageBox (a separate native dialog, not a WPF window) is up. Not worth
            // the splash-transition retries this was meant to save given it breaks dialog
            // detection outright.
            App = Application.Launch(exePath, "--nolibupdate");
        }

        /* IClassFixture<T> requires a single public constructor, so the extra beforeLaunch
           hook stays private and reachable only through this factory — used by tests that
           manage their own PlayniteFixture instance instead of sharing one via IClassFixture
           (see ManifestAutoDownloadUiTests). */
        public static PlayniteFixture LaunchFresh(Action beforeLaunch) => new PlayniteFixture(beforeLaunch);

        /* Confirmed live (2026-08-08): Playnite shows a "Please wait while the application
           opens" splash first (Name matches that literal string, 0-1 children), then during
           handoff to the real window UIA queries throw TimeoutException/
           UIA_E_ELEMENTNOTAVAILABLE for several seconds, before the real window appears as
           Name="Playnite" with 100+ children. Takes ~14s end-to-end on this machine. Checking
           only the name (not child count) returns the splash window's own empty tree — the
           name check alone isn't a sufficient readiness signal, actual children are. */
        public Window GetMainWindow(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(45));
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var window = App.GetMainWindow(Automation, TimeSpan.FromSeconds(3));
                    if (window != null)
                    {
                        if (DismissStartupErrorIfPresent(window))
                        {
                            // The dialog's own tree (Yes/No/Text/TitleBar) is non-empty too —
                            // re-fetch fresh next iteration rather than trust this stale
                            // reference, since dismissing it changes what "main window" means.
                            System.Threading.Thread.Sleep(500);
                            continue;
                        }

                        // "Playnite" is the real window's confirmed Name once fully loaded
                        // (live-tested 2026-08-08) — the splash/dialog windows never have it.
                        if (window.Name == "Playnite" && window.FindAllChildren().Length > 0)
                        {
                            return window;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                System.Threading.Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                "Playnite's real main window never became available within the timeout.",
                lastError);
        }

        /* "Playnite closed unexpectedly while starting... start in safe mode with all 3rd
           party add-ons disabled?" — Playnite's own crash-recovery prompt, seen after a prior
           hard-kill (now avoided in KillIfRunning, but a stray one from outside this fixture
           could still trigger it). Always answer No — Yes would disable our own addon, which
           defeats the point of these tests. */
        private static bool DismissStartupErrorIfPresent(AutomationElement root)
        {
            var errorWindow = root.FindFirstDescendant(cf => cf.ByName("Startup Error"));
            var noButton = errorWindow?.FindFirstDescendant(cf => cf.ByName("No"));
            if (noButton == null) return false;

            FlaUI.Core.Input.Mouse.Click(noButton.GetClickablePoint(), FlaUI.Core.Input.MouseButton.Left);
            return true;
        }

        public void Dispose()
        {
            Automation.Dispose();

            try
            {
                // Same reasoning as KillIfRunning: --shutdown goes through Playnite's own
                // Quit()/ReleaseResources() path via its pipe, rather than App.Close()'s
                // CloseMainWindow()-style approach that can silently no-op if the process
                // isn't in a state to respond to a window message right at this instant.
                using (var shutdownSignal = Process.Start(exePath, "--shutdown"))
                {
                    shutdownSignal?.WaitForExit(10_000);
                }

                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!App.HasExited && DateTime.UtcNow < deadline)
                {
                    System.Threading.Thread.Sleep(200);
                }
                if (!App.HasExited)
                {
                    App.Kill();
                }
            }
            catch
            {
                // Best-effort — the config restore below matters more than a clean shutdown.
            }
            App.Dispose();

            // Restored after Playnite has fully exited, in case it writes its own in-memory
            // settings back to config.json on shutdown — this must be the last write to win.
            PlayniteConfigAccessibility.Restore(previousAccessibilityValue);
        }

        private static string ResolveExePath()
        {
            var running = Process.GetProcessesByName("Playnite.DesktopApp").FirstOrDefault();
            if (running != null)
            {
                try { return running.MainModule.FileName; }
                catch { /* fall through to the other lookup strategies below */ }
            }

            var configured = Environment.GetEnvironmentVariable("HUM_UITEST_PLAYNITE_EXE");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            var fallback = Path.Combine(localAppData, "Playnite", "Playnite.DesktopApp.exe");
            if (File.Exists(fallback))
            {
                return fallback;
            }

            // Playnite doesn't always install under %LOCALAPPDATA% (e.g. a custom install
            // drive, or a non-Steam-game Steam shortcut, as on this machine — D:\Playnite\).
            // Its installer registers an Uninstall registry key with the real InstallLocation,
            // same trick build-and-run.ps1 uses to find it, so this isn't hardcoded to one drive.
            var fromRegistry = TryGetInstallLocationFromRegistry();
            if (fromRegistry != null)
            {
                var candidate = Path.Combine(fromRegistry, "Playnite.DesktopApp.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "Couldn't find Playnite.DesktopApp.exe. Start Playnite once yourself so its " +
                "install path can be sniffed from the running process, or set " +
                "HUM_UITEST_PLAYNITE_EXE to the full exe path.");
        }

        private static string TryGetInstallLocationFromRegistry()
        {
            string[] uninstallKeys =
            {
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };

            foreach (var rootPath in uninstallKeys)
            {
                var parts = rootPath.Split('\\');
                var hive = parts[0] switch
                {
                    "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
                    "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
                    _ => null,
                };
                if (hive == null) continue;

                var subKeyPath = string.Join("\\", parts, 1, parts.Length - 1);
                using var uninstallRoot = hive.OpenSubKey(subKeyPath);
                if (uninstallRoot == null) continue;

                foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                {
                    using var subKey = uninstallRoot.OpenSubKey(subKeyName);
                    if ((subKey?.GetValue("DisplayName") as string) == "Playnite")
                    {
                        return subKey.GetValue("InstallLocation") as string;
                    }
                }
            }

            return null;
        }

        /* Goes through Playnite's own shutdown path instead of CloseMainWindow(): --shutdown,
           launched as a brand-new short-lived process, detects the running instance's
           single-instance mutex, sends a Shutdown command over its existing named pipe, and
           the running instance handles that by calling its own Quit()/ReleaseResources() —
           confirmed by reading PlayniteApplication.cs directly, not guessed. CloseMainWindow()
           depends on the process having a responsive main window at that exact instant (e.g.
           it silently does nothing if a modal dialog has focus, or during its own startup
           window) — a real risk here since these tests routinely relaunch Playnite while it
           may still be settling from a previous run. Kill() stays as a last-resort fallback
           only: a hard-killed Playnite flags its own next launch as "closed unexpectedly" and
           shows a blocking "start in safe mode?" dialog (confirmed live 2026-08-08, and again
           2026-08-08 when a forced kill elsewhere left exactly that flag set — see
           StartupErrorDialogUiTests), which would disable our own addon if answered wrong. */
        private static void KillIfRunning(string exePath)
        {
            if (Process.GetProcessesByName("Playnite.DesktopApp").Length == 0) return;

            try
            {
                using (var shutdownSignal = Process.Start(exePath, "--shutdown"))
                {
                    shutdownSignal?.WaitForExit(10_000);
                }
            }
            catch
            {
                // Best-effort — the Kill() fallback below still catches anything left running.
            }

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && Process.GetProcessesByName("Playnite.DesktopApp").Length > 0)
            {
                System.Threading.Thread.Sleep(200);
            }

            foreach (var p in Process.GetProcessesByName("Playnite.DesktopApp"))
            {
                p.Kill();
                p.WaitForExit(15_000);
            }
        }
    }
}
