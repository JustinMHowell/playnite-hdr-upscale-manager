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
    /* Fullscreen counterpart to PlayniteFixture — launches Playnite.FullscreenApp.exe directly
       rather than passing --startfullscreen to Playnite.DesktopApp.exe (confirmed from
       reference/Playnite/source/Playnite/App/PlayniteApplication.cs that the switch just
       relaunches this same exe, so launching it directly skips a redundant hop). Kills both
       Desktop and Fullscreen processes on construction/disposal since only one can hold the
       AccessibilityInterface-dependent automation tree at a time. */
    public class PlayniteFullscreenFixture : IDisposable
    {
        public UIA3Automation Automation { get; }
        public Application App { get; }

        private readonly string exePath;
        private readonly JToken previousAccessibilityValue;
        private readonly JToken previousFilterValue;

        public PlayniteFullscreenFixture()
        {
            Automation = new UIA3Automation();
            exePath = ResolveExePath();
            KillRunningPlaynite();

            previousAccessibilityValue = PlayniteConfigAccessibility.ForceAlwaysOn();
            previousFilterValue = PlayniteFullscreenFilter.Reset();

            App = Application.Launch(exePath, "--nolibupdate");
        }

        public Window GetMainWindow(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(45));
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var window = App.GetMainWindow(Automation, TimeSpan.FromSeconds(3));
                    if (window != null && window.Name == "Playnite" && window.FindAllChildren().Length > 0)
                    {
                        return window;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                System.Threading.Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                "Playnite.FullscreenApp's main window never became available within the timeout.",
                lastError);
        }

        public void Dispose()
        {
            Automation.Dispose();

            try
            {
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
                // Best-effort cleanup only — the config restore below matters more.
            }
            App.Dispose();

            // Restored after Playnite has fully exited, in case it writes its own in-memory
            // settings back to disk on shutdown — this must be the last write to win.
            PlayniteConfigAccessibility.Restore(previousAccessibilityValue);
            PlayniteFullscreenFilter.Restore(previousFilterValue);
        }

        private static string ResolveExePath()
        {
            var running = Process.GetProcessesByName("Playnite.FullscreenApp").FirstOrDefault();
            if (running != null)
            {
                try { return running.MainModule.FileName; }
                catch { /* fall through */ }
            }

            var configured = Environment.GetEnvironmentVariable("HUM_UITEST_PLAYNITE_FULLSCREEN_EXE");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            // Fullscreen lives in the same install folder as Desktop — sniff that from
            // whichever source is available (same lookup PlayniteFixture uses), then swap the
            // file name.
            var desktopExe = ResolveDesktopExePath();
            if (desktopExe != null)
            {
                var candidate = Path.Combine(Path.GetDirectoryName(desktopExe) ?? "", "Playnite.FullscreenApp.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "Couldn't find Playnite.FullscreenApp.exe. Start Playnite (Desktop or Fullscreen) " +
                "once yourself so its install path can be sniffed, or set " +
                "HUM_UITEST_PLAYNITE_FULLSCREEN_EXE to the full exe path.");
        }

        private static string ResolveDesktopExePath()
        {
            var running = Process.GetProcessesByName("Playnite.DesktopApp").FirstOrDefault();
            if (running != null)
            {
                try { return running.MainModule.FileName; }
                catch { /* fall through */ }
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

            return TryGetInstallLocationFromRegistry() is string installDir
                ? Path.Combine(installDir, "Playnite.DesktopApp.exe")
                : null;
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

        private static void KillRunningPlaynite()
        {
            foreach (var name in new[] { "Playnite.FullscreenApp", "Playnite.DesktopApp" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); p.WaitForExit(15_000); } catch { /* best effort */ }
                }
            }
        }
    }
}
