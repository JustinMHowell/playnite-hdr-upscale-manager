using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using System;
using System.Collections.Concurrent;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        /* Remembers each game's HDR state from before OnGameStarting forced it on, so
           OnGameStopped can restore it rather than blindly turning HDR back off (e.g. if the
           user already had HDR on for other reasons). In-memory only — scoped to this Playnite
           process's lifetime, which covers a single play session. */
        private static readonly ConcurrentDictionary<Guid, bool> PreGameHdrState = new ConcurrentDictionary<Guid, bool>();

        /* The opt-in flag itself is set from the manager window (ManagerViewModel.HdrToggleEnabled)
           — this partial only reads it to drive the actual launch/exit behavior. */
        private JsonRecordStore<bool> HdrToggleOptIns =>
            new JsonRecordStore<bool>(GetPluginUserDataPath(), "hdr_toggle_opt_in.json");

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
