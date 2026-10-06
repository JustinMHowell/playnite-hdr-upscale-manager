using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HdrUpscaleManager.Plugin
{
    public partial class HdrUpscaleManagerPlugin
    {
        public const string RenoDxDedicatedModTagName = "RenoDX: Dedicated mod";

        /* Syncs the tag over the given games rather than only adding it: games that no longer
           match the wiki (or were mis-tagged by an earlier name match) lose it, so re-running
           this is how the tag gets refreshed. Games outside the given set are left alone. */
        private void TagGamesWithDedicatedRenoDxMod(List<Game> games)
        {
            if (games == null || games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("No games to tag.", "HDR & Upscale Manager");
                return;
            }

            List<RenoDxWikiMod> mods = null;
            var tagged = 0;
            var added = 0;
            var removed = 0;
            Exception failure = null;

            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    var userDataPath = GetPluginUserDataPath();
                    // Falls back to the cached list by itself when the wiki can't be reached.
                    mods = new RenoDxWikiService(Path.Combine(userDataPath, "RenoDxWikiCache"))
                        .FetchAllAsync().GetAwaiter().GetResult();
                    if (mods == null || mods.Count == 0) return;

                    RemoteManifest manifest = new ManifestService(userDataPath).TryGetCachedManifest();
                    var tag = PlayniteApi.Database.Tags.Add(RenoDxDedicatedModTagName);

                    progress.ProgressMaxValue = games.Count;
                    using (PlayniteApi.Database.BufferedUpdate())
                    {
                        foreach (var game in games)
                        {
                            if (progress.CancelToken.IsCancellationRequested) break;
                            progress.CurrentProgressValue++;

                            var hasMod = RenoDxService.HasDedicatedMod(game.Name, mods, manifest);
                            var hasTag = game.TagIds?.Contains(tag.Id) == true;
                            if (hasMod) tagged++;
                            if (hasMod == hasTag) continue;

                            if (hasMod)
                            {
                                if (game.TagIds == null) game.TagIds = new List<Guid>();
                                game.TagIds.Add(tag.Id);
                                added++;
                            }
                            else
                            {
                                game.TagIds.Remove(tag.Id);
                                removed++;
                            }

                            PlayniteApi.Database.Games.Update(game);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }, new GlobalProgressOptions("Checking games against the RenoDX wiki…", true) { IsIndeterminate = false });

            if (failure != null)
            {
                Log.Error(failure, "Tagging games with a dedicated RenoDX mod failed.");
                PlayniteApi.Dialogs.ShowErrorMessage($"Tagging failed: {failure.Message}", "HDR & Upscale Manager");
                return;
            }

            if (mods == null || mods.Count == 0)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Could not load the RenoDX wiki mod list (no network and nothing cached yet) — no tags were changed.",
                    "HDR & Upscale Manager");
                return;
            }

            Log.Info($"RenoDX dedicated-mod tagging: {tagged} of {games.Count} games match ({added} tagged, {removed} untagged).");
            PlayniteApi.Dialogs.ShowMessage(
                $"{tagged} of {games.Count} games have a dedicated RenoDX mod.\n\n" +
                $"Tag \"{RenoDxDedicatedModTagName}\": added to {added}, removed from {removed}.",
                "HDR & Upscale Manager");
        }
    }
}
