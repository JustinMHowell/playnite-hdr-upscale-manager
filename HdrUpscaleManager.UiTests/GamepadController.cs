using System;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace HdrUpscaleManager.UiTests
{
    /* Thin wrapper over Nefarius.ViGEm.Client — creates a virtual Xbox 360 controller that
       Windows' XInput layer, and therefore Playnite's own GameControllerManager, reports as a
       real gamepad. Requires the ViGEmBus driver (https://vigembus.com/) installed on the
       machine running these tests — the constructor throws if it isn't.

       If a previous run's controller wasn't disconnected cleanly, ViGEmBus can accumulate ghost
       devices until it refuses to create new ones (Connect() throwing Win32Exception "The
       operation completed successfully"). Dispose() always runs via `using`, so this shouldn't
       happen in normal use; if it does, remove the stale "Xbox 360 Controller for Windows"
       devices under Device Manager (Service = xusb22) rather than restarting the ViGEmBus
       service, which Windows refuses to stop while it still has child devices attached. */
    internal sealed class GamepadController : IDisposable
    {
        private readonly ViGEmClient client;
        private readonly IXbox360Controller controller;

        public GamepadController()
        {
            client = new ViGEmClient();
            controller = client.CreateXbox360Controller();
            controller.Connect();

            // Playnite's own controller-detection poll picks up a newly connected virtual
            // controller roughly 2-3s after Connect() returns, not instantly — a button pressed
            // before that lands on a device Playnite hasn't registered as an input source yet
            // and is silently dropped.
            Thread.Sleep(3000);
        }

        public void PressAndRelease(Xbox360Button button, int holdMs = 250, int settleMs = 200)
        {
            controller.SetButtonState(button, true);
            Thread.Sleep(holdMs);
            controller.SetButtonState(button, false);
            Thread.Sleep(settleMs);
        }

        // Xbox 360's D-pad has no separate axis the way DualShock4 does — XInput represents it
        // as ordinary button bits, so this is just PressAndRelease under a clearer name.
        public void PressDpad(Xbox360Button direction, int holdMs = 150, int settleMs = 200) =>
            PressAndRelease(direction, holdMs, settleMs);

        public void Dispose()
        {
            try { controller.Disconnect(); } catch { /* best effort */ }
            client.Dispose();
        }
    }
}
