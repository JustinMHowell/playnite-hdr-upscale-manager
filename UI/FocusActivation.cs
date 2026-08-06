using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HdrUpscaleManager.UI
{
    /* Invokes whatever ButtonBase-derived control currently holds WPF's logical focus, through
       the same UI Automation pattern a real mouse click goes through — needed because gamepad
       "confirm" doesn't arrive as a normal Enter keypress the way D-pad movement arrives as
       arrow keys; Playnite instead raises it via GenericPlugin.OnControllerButtonStateChanged,
       decoupled from WPF's own input system. Pattern reused as-is from
       playnite-eden-mod-manager (same author, GPL-3.0). */
    public static class FocusActivation
    {
        public static void InvokeFocused(DependencyObject scope)
        {
            if (!(FocusManager.GetFocusedElement(scope) is ButtonBase button) || !button.IsEnabled) return;

            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            if (peer == null) return;

            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
                invokeProvider.Invoke();
            else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggleProvider)
                toggleProvider.Toggle();
        }
    }
}
