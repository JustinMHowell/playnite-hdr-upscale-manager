using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace HdrUpscaleManager.UiTests
{
    /* Playnite Fullscreen's on-screen keyboard (opened whenever a TextBox needs keyboard-less
       text entry, e.g. the search box) doesn't reliably accept a literal SendInput keystroke
       stream — a typed string lands mixed with characters from the keyboard's own currently-
       highlighted-key state. Each character is instead typed by invoking that letter's own
       on-screen button, the same action a real key-press activates.

       Letter/digit keys expose their own character as their accessible Name directly (e.g. a
       Button named "q"). "Clear" doesn't — its Button has no Name of its own, only a nested Text
       element carries the label — so it's found by its label text and invoked via the enclosing
       Button ancestor. Submitting uses a real Enter keypress rather than invoking the on-screen
       "DONE" button's Invoke pattern — both call the same ConfirmCommand, but only the real
       keypress reliably leaves the window able to receive further real gamepad input afterward.

       This is Playnite-core's own generic TextInputWindow (Playnite.FullscreenApp/Windows/
       TextInputWindow.xaml(.cs)), reused by every Fullscreen theme for any keyboard-less text
       entry, not something specific to this machine's theme. */
    internal static class FullscreenOnScreenKeyboard
    {
        public static void Type(Window mainWindow, string text, ITestOutputHelper output)
        {
            // The field's text is a persisted setting (AppSettings.Fullscreen.FilterSettings.Name)
            // that survives a fresh Playnite relaunch — clearing it first avoids appending onto
            // whatever an earlier session last searched for.
            FindLabeledButton(mainWindow, "Clear", output).Patterns.Invoke.Pattern.Invoke();
            Thread.Sleep(200);

            foreach (var ch in text.ToLowerInvariant())
            {
                var keyButton = ch == ' '
                    ? FindLabeledButton(mainWindow, "Space", output)
                    : FindKeyButton(mainWindow, ch.ToString(), output);

                keyButton.Patterns.Invoke.Pattern.Invoke();
                Thread.Sleep(120);
            }

            // TextInputText is the actual text field the keyboard writes into — reading it back
            // confirms every invoked key actually landed as the expected character before
            // submitting, instead of trusting that N Invoke() calls produced N correct characters.
            var textBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TextInputText"));
            var actual = textBox?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
            output.WriteLine($"On-screen keyboard field after typing \"{text}\": \"{actual}\"");

            if (!string.Equals(actual, text, StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("--- automation tree dump: main window (typed text mismatch) ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException(
                    $"Expected the on-screen keyboard field to read \"{text}\" but it reads \"{actual}\".");
            }
        }

        public static void Submit(Window mainWindow, ITestOutputHelper output)
        {
            // TextInputText_PreviewKeyDown treats Key.Return as Confirm() directly — a real
            // keypress here rather than an Invoke() on the "DONE" button's Invoke pattern.
            var textBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TextInputText"));
            textBox?.Focus();
            Thread.Sleep(150);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
            Thread.Sleep(80);
            FlaUI.Core.Input.Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
        }

        private static AutomationElement FindKeyButton(Window mainWindow, string character, ITestOutputHelper output)
        {
            var button = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName(character).And(cf.ByControlType(ControlType.Button))),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (button == null)
            {
                output.WriteLine($"--- automation tree dump: main window (looking for key \"{character}\") ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException($"Couldn't find the on-screen keyboard key for '{character}'.");
            }

            return button;
        }

        private static AutomationElement FindLabeledButton(Window mainWindow, string label, ITestOutputHelper output)
        {
            var button = Retry.WhileNull(
                () => mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                    .FirstOrDefault(b => b.FindFirstDescendant(cf => cf.ByName(label)) != null),
                timeout: TimeSpan.FromSeconds(5)).Result;

            if (button == null)
            {
                output.WriteLine($"--- automation tree dump: main window (looking for \"{label}\" button) ---");
                output.WriteLine(AutomationTreeDumper.Dump(mainWindow));
                throw new InvalidOperationException($"Couldn't find the on-screen keyboard's \"{label}\" button.");
            }

            return button;
        }
    }
}
