using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        /* Remembers each game's HDR state from before OnGameStarting forced it on, so
           OnGameStopped can restore it rather than blindly turning HDR back off (e.g. if the
           user already had HDR on for other reasons). In-memory only — scoped to this Playnite
           process's lifetime, which covers a single play session. */
        private static readonly ConcurrentDictionary<Guid, bool> PreGameHdrState = new ConcurrentDictionary<Guid, bool>();

        private JsonRecordStore<bool> HdrToggleOptIns =>
            new JsonRecordStore<bool>(GetPluginUserDataPath(), "hdr_toggle_opt_in.json");

        private IEnumerable<GameMenuItem> BuildHdrToggleMenuItems(Playnite.SDK.Models.Game game)
        {
            var optedIn = HdrToggleOptIns.Get(game.Id.ToString()) == true;

            yield return new GameMenuItem
            {
                Description = optedIn
                    ? "Disable HDR toggle on launch/exit for this game"
                    : "Enable HDR toggle on launch/exit for this game (unverified — see PLAN.md)",
                MenuSection = "HDR & Upscale Manager",
                Action = _ => ToggleHdrOptIn(game, !optedIn)
            };
        }

        private void ToggleHdrOptIn(Playnite.SDK.Models.Game game, bool enable)
        {
            try
            {
                if (enable)
                    HdrToggleOptIns.Set(game.Id.ToString(), true);
                else
                    HdrToggleOptIns.Remove(game.Id.ToString());

                PlayniteApi.Dialogs.ShowMessage(
                    enable
                        ? $"HDR will be toggled on when '{game.Name}' starts and restored when it exits."
                        : $"HDR will no longer be toggled around '{game.Name}'.",
                    "HDR Toggle");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to update the HDR toggle opt-in for '{game.Name}'.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to update HDR toggle setting: {ex.Message}", "HDR Toggle");
            }
        }

        public override void OnGameStarting(Playnite.SDK.Events.OnGameStartingEventArgs args)
        {
            try
            {
                var game = args.Game;
                if (game == null || HdrToggleOptIns.Get(game.Id.ToString()) != true) return;

                if (HdrToggleService.TryGetHdrEnabled(out var wasEnabled))
                    PreGameHdrState[game.Id] = wasEnabled;

                if (!HdrToggleService.TrySetHdrEnabled(true))
                    Log.Warn($"Could not enable HDR for '{game.Name}' on launch — continuing without blocking startup.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HDR toggle on game start failed — continuing without blocking startup.");
            }
        }

        public override void OnGameStopped(Playnite.SDK.Events.OnGameStoppedEventArgs args)
        {
            try
            {
                var game = args.Game;
                if (game == null || HdrToggleOptIns.Get(game.Id.ToString()) != true) return;

                var restoreTo = PreGameHdrState.TryRemove(game.Id, out var previous) ? previous : false;
                if (!HdrToggleService.TrySetHdrEnabled(restoreTo))
                    Log.Warn($"Could not restore HDR state for '{game.Name}' on exit.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HDR toggle on game stop failed.");
            }
        }
    }
}
