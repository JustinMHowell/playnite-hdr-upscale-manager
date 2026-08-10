using HdrUpscaleManager.UI;
using Playnite.SDK;
using Playnite.SDK.Events;
using System.Windows;
using System.Windows.Controls;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        /* Tracked so OnControllerButtonStateChanged (raised regardless of which window has OS
           focus) only reacts to gamepad input while our window is actually open. */
        private Window openManagerWindow;
        private ManagerViewModel openManagerViewModel;

        /* Fullscreen gets its own view (ManagerFullscreenView) — Playnite's Fullscreen host
           window for plugin content renders chromeless/transparent, and default WPF controls
           have no visible focus state readable from a couch. Both views share the same
           ManagerViewModel — only presentation differs. Per-game only (no library-wide entry
           point), matching PLAN.md §5's original per-game-dialog design. */
        private void OpenManagerWindow(Playnite.SDK.Models.Game game)
        {
            var vm = new ManagerViewModel(PlayniteApi, GetPluginUserDataPath(), game);
            var isFullscreen = PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
            UserControl view = isFullscreen ? (UserControl)new ManagerFullscreenView(vm) : new ManagerView(vm);

            // Desktop's ManagerView never renders the gamepad hint and calling this in Desktop
            // mode crashes — see ManagerViewModel's own constructor comment for why.
            if (isFullscreen) vm.RefreshGamepadIconStyle();

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                // Fullscreen is a gamepad/couch-only surface (B closes it — see
                // OnControllerButtonStateChanged) — no mouse to hit title-bar buttons with.
                ShowMaximizeButton = !isFullscreen,
                ShowCloseButton = !isFullscreen
            });
            window.Title = $"HDR & Upscale Manager — {game.Name}";
            window.Content = view;
            window.Width = isFullscreen ? 1100 : 820;
            window.Height = isFullscreen ? 780 : 680;
            window.WindowStartupLocation = isFullscreen
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;

            openManagerWindow = window;
            openManagerViewModel = vm;
            window.Closed += (s, e) => { openManagerWindow = null; openManagerViewModel = null; };
            window.ShowDialog();
        }

        /* Gamepad "confirm"/"back" don't arrive as WM_KEYDOWN Enter/Escape the way D-pad
           movement arrives as arrow keys — Playnite raises them here instead, decoupled from
           WPF's own input/focus system. Guarded on openManagerWindow so this doesn't react to
           A/B presses made anywhere else in Playnite while our window happens to not be open. */
        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (openManagerWindow == null || args.State != ControllerInputState.Pressed) return;

            switch (args.Button)
            {
                case ControllerInput.B:
                case ControllerInput.Back:
                    openManagerWindow.Close();
                    break;
                case ControllerInput.A:
                    FocusActivation.InvokeFocused(openManagerWindow);
                    break;
            }
        }

        /* Playnite has no controller-brand API — GamepadIconStyleDetector only has a device Name
           string to go on (see ManagerViewModel.RefreshGamepadIconStyle) — but connect/disconnect
           IS reported live, so the Fullscreen A/B icon hint can react to a controller being
           swapped while the window is already open, not just whatever was connected when it
           opened. */
        public override void OnControllerConnected(OnControllerConnectedArgs args) =>
            openManagerViewModel?.SetGamepadIconStyle(args.Controller?.Name);

        public override void OnControllerDisconnected(OnControllerDisconnectedArgs args) =>
            openManagerViewModel?.HandleControllerDisconnected(args.Controller?.Name);
    }
}
