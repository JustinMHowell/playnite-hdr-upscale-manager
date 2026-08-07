using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace HdrUpscaleManager.UiTests
{
    /* Shared across every test in a class via IClassFixture<T> so Playnite is only
       launched/attached once per test run, not once per [Fact]. Never kills an
       already-running Playnite — this drives your real, already-open library. */
    public class PlayniteFixture : IDisposable
    {
        public UIA3Automation Automation { get; }
        public Application App { get; }

        private readonly bool ownsProcess;

        public PlayniteFixture()
        {
            Automation = new UIA3Automation();

            var running = Process.GetProcessesByName("Playnite.DesktopApp").FirstOrDefault()
                ?? Process.GetProcessesByName("Playnite.FullscreenApp").FirstOrDefault();
            if (running != null)
            {
                App = Application.Attach(running);
                ownsProcess = false;
                return;
            }

            var exePath = Environment.GetEnvironmentVariable("HUM_UITEST_PLAYNITE_EXE") ?? DefaultPlayniteExePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                throw new InvalidOperationException(
                    "Playnite isn't running and no executable was found at '" + exePath + "'. " +
                    "Either start Playnite yourself first (recommended — run build-and-run.ps1 to deploy " +
                    "the addon build), or set HUM_UITEST_PLAYNITE_EXE to the full path of Playnite.DesktopApp.exe.");
            }

            App = Application.Launch(exePath);
            ownsProcess = true;
        }

        public Window GetMainWindow(TimeSpan? timeout = null) =>
            App.GetMainWindow(Automation, timeout ?? TimeSpan.FromSeconds(30));

        public void Dispose()
        {
            Automation.Dispose();
            if (ownsProcess)
            {
                App.Close();
            }
            App.Dispose();
        }

        private static string DefaultPlayniteExePath()
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            return Path.Combine(localAppData, "Playnite", "Playnite.DesktopApp.exe");
        }
    }
}
