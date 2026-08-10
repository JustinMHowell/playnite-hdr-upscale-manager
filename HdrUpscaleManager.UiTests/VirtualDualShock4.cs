using System;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace HdrUpscaleManager.UiTests
{
    /* Counterpart to GamepadController, but a DualShock4 target (Sony's real VID_054C/PID_05C4)
       instead of an Xbox 360 one — used only to test GamepadIconStyleDetector's brand guess
       against a real, differently-branded virtual device; no button-press API needed since that
       detection reads the device's reported Name, not its inputs. Same ViGEmBus driver
       requirement and ghost-device caveat as GamepadController. */
    internal sealed class VirtualDualShock4 : IDisposable
    {
        private readonly ViGEmClient client;
        private readonly IDualShock4Controller controller;

        public VirtualDualShock4()
        {
            client = new ViGEmClient();
            controller = client.CreateDualShock4Controller();
            controller.Connect();

            // Same settle time GamepadController waits for Playnite's own controller-detection
            // poll to notice a newly connected virtual device.
            Thread.Sleep(3000);
        }

        public void Dispose()
        {
            try { controller.Disconnect(); } catch { /* best effort */ }
            client.Dispose();
        }
    }
}
