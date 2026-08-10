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

        private ManagerViewModel Vm => (ManagerViewModel)DataContext;

        /* Checked/Unchecked fire for every cause that flips IsChecked — a real press (mouse or
           gamepad A via FocusActivation's IToggleProvider.Toggle(), see UI/FocusActivation.cs)
           AND the OneWay binding correcting the toggle back if the install/uninstall it just
           triggered failed. CanExecute-gating (rather than a reentrancy flag) makes both cases
           safe with the same code: a correction back to "not installed" finds UninstallCommand
           already un-executable (no record exists) and is a no-op. */
        private static void ExecuteIfPossible(ICommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }

        private void ReShadeToggle_Checked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.ReShadeInstallCommand);
        private void ReShadeToggle_Unchecked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.ReShadeUninstallCommand);

        private void LumaToggle_Checked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.LumaInstallCommand);
        private void LumaToggle_Unchecked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.LumaUninstallCommand);

        private void DxvkToggle_Checked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.DxvkInstallCommand);
        private void DxvkToggle_Unchecked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.DxvkUninstallCommand);

        private void RenoDxToggle_Checked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.RenoDxInstallCommand);
        private void RenoDxToggle_Unchecked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.RenoDxUninstallCommand);

        /* OptiScaler has no single "install" command — DLSS-only vs has-FSR/XeSS is a real
           choice, not a fallback like RenoDX's engine-detect buttons, so this toggle is bound
           IsEnabled={Binding IsOptiScalerInstalled} in XAML and only ever reachable to turn OFF;
           installing still goes through the two variant buttons above it. */
        private void OptiScalerToggle_Unchecked(object sender, RoutedEventArgs e) => ExecuteIfPossible(Vm.OptiScalerUninstallCommand);
    }
}
