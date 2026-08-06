using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HdrUpscaleManager.UI
{
    public partial class ManagerFullscreenView : UserControl
    {
        public ManagerFullscreenView(ManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            /* Playnite's Fullscreen host window doesn't hand this content keyboard/gamepad
               focus on its own — without this, nothing is focused when the window opens, so
               gamepad "confirm" has no target to activate until the mouse is used first. */
            Loaded += (s, e) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

            /* Covers a real keyboard's Enter/Escape keys. Gamepad A/B do NOT arrive here — they
               bypass WPF's input system entirely and are handled via
               HdrUpscaleManagerPlugin.OnControllerButtonStateChanged (see FocusActivation). */
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Window.GetWindow(this)?.Close();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    FocusActivation.InvokeFocused(this);
                    e.Handled = true;
                    return;
                }

                /* D-pad up/down/left/right move WPF's logical focus, but nothing auto-scrolls to
                   follow it — wait for the framework's own navigation to land before asking what's
                   now focused. */
                if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (FocusManager.GetFocusedElement(this) is FrameworkElement focused)
                            focused.BringIntoView();
                    }), DispatcherPriority.Input);
                }
            };
        }
    }
}
