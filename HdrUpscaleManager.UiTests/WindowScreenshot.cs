using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Captures just the manager window (not the whole screen) when a wait times out — call this
       BEFORE any cleanup step that might close the window, so the actual failing visual state is
       preserved instead of only a text automation-tree dump. Saved under TestScreenshots/
       (gitignored — local debugging aid only, never committed). */
    internal static class WindowScreenshot
    {
        private static readonly string ScreenshotDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestScreenshots");

        public static string CaptureOnTimeout(AutomationElement element, string testName, ITestOutputHelper output)
        {
            try
            {
                var dir = Path.GetFullPath(ScreenshotDir);
                Directory.CreateDirectory(dir);
                var fileName = $"{testName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(dir, fileName);

                using (var image = Capture.Element(element))
                {
                    image.ToFile(path);
                }

                output.WriteLine($"Screenshot saved: {path}");
                return path;
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to capture screenshot: {ex.Message}");
                return null;
            }
        }
    }
}
