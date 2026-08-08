using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services;
using HdrUpscaleManager.Services.Addons;
using HdrUpscaleManager.Services.ReShade;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace HdrUpscaleManager.UI
{
    /* Shared by both the Desktop and Fullscreen views (ManagerView.xaml / ManagerFullscreenView.xaml)
       — one per-game dialog covering every addon this plugin manages, per PLAN.md §5's original
       design. Every action logs on failure (Log.Error/Warn) and also surfaces a message to the
       user — via inline StatusText for expected outcomes (not found, blacklisted, etc.), and an
       additional ShowErrorMessage popup for unexpected exceptions, so a failure is never silent. */
    public class ManagerViewModel : ObservableObject
    {
        private static readonly HashSet<GraphicsApi> DxvkSupportedApis =
            new HashSet<GraphicsApi> { GraphicsApi.D3D8, GraphicsApi.D3D9, GraphicsApi.D3D10 };

        private readonly IPlayniteAPI api;
        private readonly string userDataPath;

        /* Detected at window-open time (install folder doesn't change while the window is open)
           — drives the RenoDX per-engine and DXVK IsEnabled bindings below. Local filesystem/PE
           detection runs synchronously first (instant); if it can't tell — usually because the
           game isn't installed yet — a PCGamingWiki lookup fills the gap in the background, and
           these fields update in place once it returns (see RefreshDetectionFromPcGamingWikiAsync). */
        private RenoDxEngine? detectedRenoDxEngine;
        private GraphicsApi detectedDxvkApi;
        private string pcgwEngineRaw;
        private bool pcgwLookupInProgress;

        public Game Game { get; }
        public string GameName => Game.Name;

        public bool CanInstallUnrealRenoDx => detectedRenoDxEngine == null || detectedRenoDxEngine == RenoDxEngine.UnrealEngine;
        public bool CanInstallUnityRenoDx => detectedRenoDxEngine == null || detectedRenoDxEngine == RenoDxEngine.UnityEngine;
        public string RenoDxEngineDetectionText => detectedRenoDxEngine switch
        {
            RenoDxEngine.UnrealEngine => "Detected engine: Unreal Engine.",
            RenoDxEngine.UnityEngine => "Detected engine: Unity.",
            _ when pcgwLookupInProgress => "Engine not auto-detected locally — checking PCGamingWiki…",
            _ when !string.IsNullOrEmpty(pcgwEngineRaw) =>
                $"Engine not auto-detected locally — PCGamingWiki lists \"{pcgwEngineRaw}\" (not a RenoDX-supported engine); both options remain available.",
            _ => "Engine not auto-detected — both Unreal and Unity options are available."
        };

        public bool CanInstallDxvk => DxvkSupportedApis.Contains(detectedDxvkApi);
        public string DxvkApiDetectionText => detectedDxvkApi switch
        {
            GraphicsApi.D3D8 => "Detected graphics API: DirectX 8.",
            GraphicsApi.D3D9 => "Detected graphics API: DirectX 9.",
            GraphicsApi.D3D10 => "Detected graphics API: DirectX 10.",
            GraphicsApi.Unknown when pcgwLookupInProgress => "Graphics API not detected locally — checking PCGamingWiki…",
            GraphicsApi.Unknown => "Graphics API not detected (game not installed, or its executable couldn't be read, and PCGamingWiki had no confident answer) — install disabled.",
            _ => $"Detected graphics API: {detectedDxvkApi} — DXVK+Lilium HDR isn't offered for this API."
        };

        private string statusText = "Ready.";
        public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

        private bool isBusy;
        public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }

        private string reShadeStatus = "Not tracked — installing always fetches the latest version.";
        public string ReShadeStatus { get => reShadeStatus; private set => SetProperty(ref reShadeStatus, value); }

        private string renoDxStatus;
        public string RenoDxStatus { get => renoDxStatus; private set => SetProperty(ref renoDxStatus, value); }

        private string renoDxExternalUrl;
        public string RenoDxExternalUrl { get => renoDxExternalUrl; private set => SetProperty(ref renoDxExternalUrl, value); }

        private string lumaStatus;
        public string LumaStatus { get => lumaStatus; private set => SetProperty(ref lumaStatus, value); }

        private string dxvkStatus;
        public string DxvkStatus { get => dxvkStatus; private set => SetProperty(ref dxvkStatus, value); }

        private string optiScalerStatus;
        public string OptiScalerStatus { get => optiScalerStatus; private set => SetProperty(ref optiScalerStatus, value); }

        private bool hdrToggleEnabled;
        public bool HdrToggleEnabled
        {
            get => hdrToggleEnabled;
            set
            {
                if (!SetProperty(ref hdrToggleEnabled, value)) return;
                try
                {
                    var store = new JsonRecordStore<bool>(userDataPath, "hdr_toggle_opt_in.json");
                    if (value) store.Set(Game.Id.ToString(), true); else store.Remove(Game.Id.ToString());
                    StatusText = value
                        ? "HDR will be toggled on when this game starts and restored when it exits."
                        : "HDR toggle disabled for this game.";
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to update HDR toggle opt-in for '{GameName}'.");
                    StatusText = $"Failed to update HDR toggle setting: {ex.Message}";
                    api.Dialogs.ShowErrorMessage(StatusText, "HDR Toggle");
                }
            }
        }

        public ICommand ReShadeInstallCommand { get; }

        public ICommand RenoDxInstallCommand { get; }
        public ICommand RenoDxInstallUnrealGenericCommand { get; }
        public ICommand RenoDxInstallUnrealUeExtendedCommand { get; }
        public ICommand RenoDxInstallUnityCommand { get; }
        public ICommand RenoDxUpdateCommand { get; }
        public ICommand RenoDxUninstallCommand { get; }
        public ICommand OpenRenoDxExternalUrlCommand { get; }

        public ICommand LumaInstallCommand { get; }
        public ICommand LumaUpdateCommand { get; }
        public ICommand LumaUninstallCommand { get; }

        public ICommand DxvkInstallCommand { get; }
        public ICommand DxvkUpdateCommand { get; }
        public ICommand DxvkUninstallCommand { get; }

        public ICommand OptiScalerInstallDlssCommand { get; }
        public ICommand OptiScalerInstallNoDlssCommand { get; }
        public ICommand OptiScalerUpdateCommand { get; }
        public ICommand OptiScalerUninstallCommand { get; }

        public ManagerViewModel(IPlayniteAPI api, string userDataPath, Game game)
        {
            this.api = api;
            this.userDataPath = userDataPath;
            Game = game;

            var cachedManifest = new ManifestService(userDataPath).TryGetCachedManifest();
            detectedRenoDxEngine = RenoDxEngineDetector.Detect(
                Game.InstallDirectory, GameName, cachedManifest?.InstallPathOverrides, cachedManifest?.EngineHintOverrides).Engine;

            var exePath = PeInspector.FindGameExecutable(Game.InstallDirectory);
            detectedDxvkApi = exePath != null ? (PeInspector.Inspect(exePath)?.Api ?? GraphicsApi.Unknown) : GraphicsApi.Unknown;

            ReShadeInstallCommand = new RelayCommand(() => Run(InstallReShadeAsync), () => !IsBusy);

            RenoDxInstallCommand = new RelayCommand(() => Run(InstallRenoDxAsync), () => !IsBusy);
            RenoDxInstallUnrealGenericCommand = new RelayCommand(
                () => Run(() => InstallRenoDxEngineAsync(RenoDxEngine.UnrealEngine, forceUeExtended: null)),
                () => !IsBusy && CanInstallUnrealRenoDx);
            RenoDxInstallUnrealUeExtendedCommand = new RelayCommand(
                () => Run(() => InstallRenoDxEngineAsync(RenoDxEngine.UnrealEngine, forceUeExtended: true)),
                () => !IsBusy && CanInstallUnrealRenoDx);
            RenoDxInstallUnityCommand = new RelayCommand(
                () => Run(() => InstallRenoDxEngineAsync(RenoDxEngine.UnityEngine, forceUeExtended: false)),
                () => !IsBusy && CanInstallUnityRenoDx);
            RenoDxUpdateCommand = new RelayCommand(() => Run(CheckRenoDxUpdateAsync), () => !IsBusy && RenoDxRecords.Get(GameId) != null);
            RenoDxUninstallCommand = new RelayCommand(() => Run(UninstallRenoDxAsync), () => !IsBusy && RenoDxRecords.Get(GameId) != null);
            OpenRenoDxExternalUrlCommand = new RelayCommand(OpenRenoDxExternalUrl, () => RenoDxExternalUrl != null);

            LumaInstallCommand = new RelayCommand(() => Run(InstallLumaAsync), () => !IsBusy);
            LumaUpdateCommand = new RelayCommand(() => Run(CheckLumaUpdateAsync), () => !IsBusy && LumaRecords.Get(GameId) != null);
            LumaUninstallCommand = new RelayCommand(() => Run(UninstallLumaAsync), () => !IsBusy && LumaRecords.Get(GameId) != null);

            DxvkInstallCommand = new RelayCommand(() => Run(InstallDxvkAsync), () => !IsBusy && CanInstallDxvk);
            DxvkUpdateCommand = new RelayCommand(() => Run(CheckDxvkUpdateAsync), () => !IsBusy && DxvkRecords.Get(GameId) != null);
            DxvkUninstallCommand = new RelayCommand(() => Run(UninstallDxvkAsync), () => !IsBusy && DxvkRecords.Get(GameId) != null);

            OptiScalerInstallDlssCommand = new RelayCommand(() => Run(() => InstallOptiScalerAsync(dlssInputs: true)), () => !IsBusy);
            OptiScalerInstallNoDlssCommand = new RelayCommand(() => Run(() => InstallOptiScalerAsync(dlssInputs: false)), () => !IsBusy);
            OptiScalerUpdateCommand = new RelayCommand(() => Run(CheckOptiScalerUpdateAsync), () => !IsBusy && OptiScalerRecords.Get(GameId) != null);
            OptiScalerUninstallCommand = new RelayCommand(() => Run(UninstallOptiScalerAsync), () => !IsBusy && OptiScalerRecords.Get(GameId) != null);

            RefreshStatuses();

            if (detectedRenoDxEngine == null || detectedDxvkApi == GraphicsApi.Unknown)
            {
                pcgwLookupInProgress = true;
                _ = RefreshDetectionFromPcGamingWikiAsync();
            }
        }

        /* Fire-and-forget from the constructor — local detection already ran synchronously above,
           this only fills in whichever of the two came back inconclusive. Every bound property
           that depends on the two detected-* fields gets an explicit OnPropertyChanged, and
           CommandManager is nudged directly since RelayCommand's CanExecute re-evaluation is tied
           to CommandManager.RequerySuggested (fires on focus/keyboard/mouse), which nothing raises
           on its own when a background task finishes with no user interaction. */
        private async Task RefreshDetectionFromPcGamingWikiAsync()
        {
            try
            {
                var cacheDir = Path.Combine(userDataPath, "PcGamingWikiCache");
                var lookup = await new PcGamingWikiService(cacheDir).LookupAsync(GameName).ConfigureAwait(true);

                if (detectedRenoDxEngine == null && lookup.MappedEngine != null)
                    detectedRenoDxEngine = lookup.MappedEngine;
                pcgwEngineRaw = lookup.EngineRaw;

                if (detectedDxvkApi == GraphicsApi.Unknown && lookup.ConfidentGraphicsApi != null)
                    detectedDxvkApi = lookup.ConfidentGraphicsApi.Value;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"PCGamingWiki fallback detection failed for '{GameName}'.");
            }
            finally
            {
                pcgwLookupInProgress = false;
                OnPropertyChanged(nameof(RenoDxEngineDetectionText));
                OnPropertyChanged(nameof(CanInstallUnrealRenoDx));
                OnPropertyChanged(nameof(CanInstallUnityRenoDx));
                OnPropertyChanged(nameof(DxvkApiDetectionText));
                OnPropertyChanged(nameof(CanInstallDxvk));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string GameId => Game.Id.ToString();

        private JsonRecordStore<RenoDxInstalledRecord> RenoDxRecords => new JsonRecordStore<RenoDxInstalledRecord>(userDataPath, "renodx_installs.json");
        private JsonRecordStore<LumaInstalledRecord> LumaRecords => new JsonRecordStore<LumaInstalledRecord>(userDataPath, "luma_installs.json");
        private JsonRecordStore<DxvkInstalledRecord> DxvkRecords => new JsonRecordStore<DxvkInstalledRecord>(userDataPath, "dxvk_installs.json");
        private JsonRecordStore<OptiScalerInstalledRecord> OptiScalerRecords => new JsonRecordStore<OptiScalerInstalledRecord>(userDataPath, "optiscaler_installs.json");

        private void RefreshStatuses()
        {
            var renoDx = RenoDxRecords.Get(GameId);
            RenoDxStatus = renoDx != null ? $"Installed ({renoDx.MatchedWikiName ?? renoDx.AddonFileName})" : "Not installed.";

            var luma = LumaRecords.Get(GameId);
            LumaStatus = luma != null ? $"Installed (build {luma.InstalledBuildNumber})" : "Not installed.";

            var dxvk = DxvkRecords.Get(GameId);
            DxvkStatus = dxvk != null ? $"Installed ({dxvk.Version}, {string.Join("+", dxvk.InstalledDlls)})" : "Not installed.";

            var optiScaler = OptiScalerRecords.Get(GameId);
            OptiScalerStatus = optiScaler != null
                ? $"Installed ({optiScaler.Version}, {(optiScaler.DlssInputs ? "DLSS-only mode" : "native upscaler mode")})"
                : "Not installed.";

            hdrToggleEnabled = new JsonRecordStore<bool>(userDataPath, "hdr_toggle_opt_in.json").Get(GameId);
            OnPropertyChanged(nameof(HdrToggleEnabled));
        }

        private async void Run(Func<Task> action)
        {
            IsBusy = true;
            try
            {
                await action().ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
                RefreshStatuses();
            }
        }

        private void OpenRenoDxExternalUrl()
        {
            if (RenoDxExternalUrl == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(RenoDxExternalUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to open RenoDX external link for '{GameName}'.");
                StatusText = $"Failed to open link: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install RenoDX");
            }
        }

        // ── ReShade ──────────────────────────────────────────────────────────────

        private async Task InstallReShadeAsync()
        {
            StatusText = "Installing ReShade…";
            try
            {
                var cacheDir = Path.Combine(userDataPath, "ReShadeCache");
                var result = await new ReShadeInstallService(cacheDir).InstallAsync(Game.InstallDirectory).ConfigureAwait(true);
                if (!result.Success)
                {
                    Log.Error($"ReShade install failed for '{GameName}': {result.Error}");
                    StatusText = result.Error;
                    return;
                }

                StatusText = $"Installed ReShade {result.Version} ({result.Api}).";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"ReShade install failed for '{GameName}'.");
                StatusText = $"ReShade install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install ReShade");
            }
        }

        // ── RenoDX ───────────────────────────────────────────────────────────────

        /* Best-effort single button: try the wiki-matched build first, and if there's simply no
           wiki entry for this game (as opposed to a blacklist/external-link outcome), fall back
           to auto-detecting the engine and installing the generic build — the granular per-mode
           menu items (matched/auto/Unreal/UE-Extended/Unity) remain available from the game's
           right-click menu for anyone who wants explicit control. */
        private async Task InstallRenoDxAsync()
        {
            StatusText = "Installing RenoDX (matched game)…";
            RenoDxExternalUrl = null;
            try
            {
                var manifestResult = await new ManifestService(userDataPath).FetchAsync().ConfigureAwait(true);
                var manifest = manifestResult?.Manifest;
                var wikiCacheDir = Path.Combine(userDataPath, "RenoDxWikiCache");

                var matched = await RenoDxService
                    .InstallForGameAsync(Game.InstallDirectory, GameName, wikiCacheDir, manifest?.WikiNameOverrides)
                    .ConfigureAwait(true);

                if (matched.Success)
                {
                    SaveRenoDxRecord(matched);
                    StatusText = $"Installed RenoDX ({matched.MatchedGameName})." + (matched.Notes != null ? $" Note: {matched.Notes}" : "");
                    return;
                }

                if (matched.ExternalUrl != null)
                {
                    RenoDxExternalUrl = matched.ExternalUrl;
                    StatusText = $"{matched.Error} Use \"Open link\" to get it manually.";
                    return;
                }

                if (matched.MatchedGameName != null)
                {
                    // Matched the wiki but had no download at all (not even external) — nothing more to try.
                    StatusText = matched.Error;
                    return;
                }

                StatusText = "No wiki match — trying engine auto-detect for a generic install…";
                var detection = RenoDxEngineDetector.Detect(
                    Game.InstallDirectory, GameName, manifest?.InstallPathOverrides, manifest?.EngineHintOverrides);
                if (detection.Engine == null)
                {
                    StatusText = "No RenoDX wiki match and couldn't auto-detect the engine — use the right-click menu's engine-specific install options instead.";
                    return;
                }

                var useUeExtended = detection.Engine == RenoDxEngine.UnrealEngine && RenoDxService.ShouldUseUeExtended(GameName, manifest);
                var generic = await RenoDxService
                    .InstallGenericAsync(detection.InstallPath, detection.Engine.Value, useUeExtended, manifest)
                    .ConfigureAwait(true);

                if (!generic.Success)
                {
                    StatusText = generic.Error;
                    return;
                }

                StatusText = $"Detected {detection.Engine} — installed generic RenoDX build.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX install failed for '{GameName}'.");
                StatusText = $"RenoDX install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install RenoDX");
            }
        }

        /* Explicit-control counterpart to InstallRenoDxAsync's best-effort button — forces a
           specific engine build (and, for Unreal, a specific UE-Extended choice) rather than
           auto-detecting. No RenoDxRecords entry is written here: generic engine installs have
           no wiki version to track, so update-check/uninstall stay scoped to matched installs
           only, same as before this moved from the game's right-click menu into this window. */
        private async Task InstallRenoDxEngineAsync(RenoDxEngine engine, bool? forceUeExtended)
        {
            var engineLabel = engine == RenoDxEngine.UnrealEngine
                ? (forceUeExtended == true ? "Unreal Engine, UE-Extended" : "Unreal Engine, generic")
                : "Unity, generic";
            StatusText = $"Installing RenoDX ({engineLabel})…";
            try
            {
                RemoteManifest manifest = null;
                if (engine == RenoDxEngine.UnrealEngine)
                    manifest = (await new ManifestService(userDataPath).FetchAsync().ConfigureAwait(true))?.Manifest;

                var useUeExtended = forceUeExtended ?? RenoDxService.ShouldUseUeExtended(GameName, manifest);
                var result = await RenoDxService
                    .InstallGenericAsync(Game.InstallDirectory, engine, useUeExtended, manifest)
                    .ConfigureAwait(true);

                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                StatusText = $"Installed RenoDX ({engineLabel}).";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX {engineLabel} install failed for '{GameName}'.");
                StatusText = $"RenoDX install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install RenoDX");
            }
        }

        private void SaveRenoDxRecord(RenoDxMatchedInstallResult result)
        {
            RenoDxRecords.Set(GameId, new RenoDxInstalledRecord
            {
                GameId = GameId,
                GameName = GameName,
                InstallPath = result.InstalledPath,
                AddonFileName = result.AddonFileName,
                MatchedWikiName = result.MatchedGameName,
                DownloadUrl = result.DownloadUrl,
                FileHash = result.FileHash,
                RemoteContentLength = result.RemoteContentLength,
                RemoteLastModified = result.RemoteLastModified,
                InstalledAtUtc = DateTimeOffset.UtcNow
            });
        }

        private async Task CheckRenoDxUpdateAsync()
        {
            var record = RenoDxRecords.Get(GameId);
            if (record == null) return;

            StatusText = "Checking for a RenoDX update…";
            try
            {
                var manifestResult = await new ManifestService(userDataPath).FetchAsync().ConfigureAwait(true);
                var wikiCacheDir = Path.Combine(userDataPath, "RenoDxWikiCache");
                var check = await RenoDxService
                    .CheckForUpdateAsync(record, wikiCacheDir, manifestResult?.Manifest?.WikiNameOverrides)
                    .ConfigureAwait(true);

                StatusText = check.Status switch
                {
                    RenoDxUpdateStatus.UpToDate => "RenoDX is already up to date.",
                    RenoDxUpdateStatus.UpdateAvailable => "A newer RenoDX snapshot is available — reinstall to update.",
                    RenoDxUpdateStatus.NoLongerListed => "This game no longer has a matching RenoDX wiki entry.",
                    _ => "Unknown update status."
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX update check failed for '{GameName}'.");
                StatusText = $"RenoDX update check failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "RenoDX Update Check");
            }
        }

        private async Task UninstallRenoDxAsync()
        {
            var record = RenoDxRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var result = RenoDxService.Uninstall(record);
                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                RenoDxRecords.Remove(GameId);
                StatusText = "Uninstalled RenoDX.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"RenoDX uninstall failed for '{GameName}'.");
                StatusText = $"RenoDX uninstall failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Uninstall RenoDX");
            }

            await Task.CompletedTask;
        }

        // ── Luma ─────────────────────────────────────────────────────────────────

        private async Task InstallLumaAsync()
        {
            StatusText = "Installing Luma (matched game)…";
            try
            {
                var manifestResult = await new ManifestService(userDataPath).FetchAsync().ConfigureAwait(true);
                var manifest = manifestResult?.Manifest;

                var renoDxRecord = RenoDxRecords.Get(GameId);
                if (renoDxRecord != null && manifest?.IsLumaRenodxCompatible(GameName) != true)
                {
                    StatusText = "RenoDX is currently installed — uninstall it first before installing Luma (this game isn't on the manifest's dual-compat list).";
                    return;
                }

                var wikiCacheDir = Path.Combine(userDataPath, "LumaWikiCache");
                var result = await LumaService
                    .InstallForGameAsync(Game.InstallDirectory, GameName, wikiCacheDir, manifest?.LumaNameOverrides)
                    .ConfigureAwait(true);

                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                LumaRecords.Set(GameId, new LumaInstalledRecord
                {
                    GameId = GameId,
                    GameName = GameName,
                    InstallPath = result.InstalledPath,
                    InstalledFiles = result.InstalledFiles,
                    DownloadUrl = result.DownloadUrl,
                    InstalledBuildNumber = await LumaService.GetLatestBuildNumberAsync().ConfigureAwait(true) ?? 0,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                StatusText = $"Installed Luma ({result.MatchedGameName})." + (result.Notes != null ? $" Note: {result.Notes}" : "");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma install failed for '{GameName}'.");
                StatusText = $"Luma install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install Luma");
            }
        }

        private async Task CheckLumaUpdateAsync()
        {
            var record = LumaRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var check = await LumaService.CheckForUpdateAsync(record).ConfigureAwait(true);
                StatusText = check.Status switch
                {
                    LumaUpdateStatus.UpToDate => $"Luma is already up to date (build {record.InstalledBuildNumber}).",
                    LumaUpdateStatus.UpdateAvailable => $"A newer Luma build is available ({record.InstalledBuildNumber} -> {check.LatestBuildNumber}).",
                    LumaUpdateStatus.UnknownLocalInstall => "This Luma install has no known build number to compare.",
                    LumaUpdateStatus.CouldNotCheck => "Could not reach the Luma Framework release feed.",
                    _ => "Unknown update status."
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma update check failed for '{GameName}'.");
                StatusText = $"Luma update check failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Luma Update Check");
            }
        }

        private async Task UninstallLumaAsync()
        {
            var record = LumaRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var result = LumaService.Uninstall(record);
                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                LumaRecords.Remove(GameId);
                StatusText = "Uninstalled Luma.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Luma uninstall failed for '{GameName}'.");
                StatusText = $"Luma uninstall failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Uninstall Luma");
            }

            await Task.CompletedTask;
        }

        // ── DXVK+Lilium ──────────────────────────────────────────────────────────

        private async Task InstallDxvkAsync()
        {
            StatusText = "Installing DXVK+Lilium HDR…";
            try
            {
                var cacheDir = Path.Combine(userDataPath, "DxvkCache");
                var result = await DxvkService.InstallAsync(Game.InstallDirectory, cacheDir).ConfigureAwait(true);
                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                DxvkRecords.Set(GameId, new DxvkInstalledRecord
                {
                    GameId = GameId,
                    GameName = GameName,
                    InstallPath = Game.InstallDirectory,
                    InstalledDlls = result.InstalledDlls,
                    BackedUpFiles = result.BackedUpFiles,
                    DeployedConf = result.DeployedConf,
                    Version = result.Version,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                StatusText = $"Installed DXVK+Lilium HDR {result.Version}. {result.Warning}";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium install failed for '{GameName}'.");
                StatusText = $"DXVK+Lilium install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install DXVK+Lilium HDR");
            }
        }

        private async Task CheckDxvkUpdateAsync()
        {
            var record = DxvkRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var check = await DxvkService.CheckForUpdateAsync(record).ConfigureAwait(true);
                StatusText = check.Status switch
                {
                    DxvkUpdateStatus.UpToDate => $"DXVK+Lilium is already up to date ({record.Version}).",
                    DxvkUpdateStatus.UpdateAvailable => $"A newer DXVK+Lilium build is available ({record.Version} -> {check.LatestVersion}).",
                    DxvkUpdateStatus.CouldNotCheck => "Could not reach the Lilium HDR DXVK release feed.",
                    _ => "Unknown update status."
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium update check failed for '{GameName}'.");
                StatusText = $"DXVK+Lilium update check failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "DXVK+Lilium Update Check");
            }
        }

        private async Task UninstallDxvkAsync()
        {
            var record = DxvkRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var result = DxvkService.Uninstall(record);
                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                DxvkRecords.Remove(GameId);
                StatusText = "Uninstalled DXVK+Lilium.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DXVK+Lilium uninstall failed for '{GameName}'.");
                StatusText = $"DXVK+Lilium uninstall failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Uninstall DXVK+Lilium");
            }

            await Task.CompletedTask;
        }

        // ── OptiScaler ───────────────────────────────────────────────────────────

        private async Task InstallOptiScalerAsync(bool dlssInputs)
        {
            StatusText = "Installing OptiScaler…";
            try
            {
                var manifestResult = await new ManifestService(userDataPath).FetchAsync().ConfigureAwait(true);
                var cacheDir = Path.Combine(userDataPath, "OptiScalerCache");

                var result = await OptiScalerService
                    .InstallAsync(Game.InstallDirectory, GameName, dlssInputs, cacheDir, manifestResult?.Manifest)
                    .ConfigureAwait(true);

                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                OptiScalerRecords.Set(GameId, new OptiScalerInstalledRecord
                {
                    GameId = GameId,
                    GameName = GameName,
                    InstallPath = Game.InstallDirectory,
                    DllName = result.DllName,
                    InstalledFiles = result.InstalledFiles,
                    DlssInputs = dlssInputs,
                    OptiPatcherInstalled = result.OptiPatcherInstalled,
                    DlssDllInstalled = result.DlssDllInstalled,
                    Version = result.Version,
                    InstalledAtUtc = DateTimeOffset.UtcNow
                });

                StatusText = $"Installed OptiScaler {result.Version} ({result.DllName}). " +
                    $"OptiPatcher: {(result.OptiPatcherInstalled ? "yes" : "no")}. " +
                    $"DLSS-input DLL: {(result.DlssDllInstalled ? "staged" : "not staged")}.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler install failed for '{GameName}'.");
                StatusText = $"OptiScaler install failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Install OptiScaler");
            }
        }

        private async Task CheckOptiScalerUpdateAsync()
        {
            var record = OptiScalerRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var check = await OptiScalerService.CheckForUpdateAsync(record).ConfigureAwait(true);
                StatusText = check.Status switch
                {
                    OptiScalerUpdateStatus.UpToDate => $"OptiScaler is already up to date ({record.Version}).",
                    OptiScalerUpdateStatus.UpdateAvailable => $"A newer OptiScaler build is available ({record.Version} -> {check.LatestVersion}).",
                    OptiScalerUpdateStatus.CouldNotCheck => "Could not reach the OptiScaler release feed.",
                    _ => "Unknown update status."
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler update check failed for '{GameName}'.");
                StatusText = $"OptiScaler update check failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "OptiScaler Update Check");
            }
        }

        private async Task UninstallOptiScalerAsync()
        {
            var record = OptiScalerRecords.Get(GameId);
            if (record == null) return;

            try
            {
                var result = OptiScalerService.Uninstall(record);
                if (!result.Success)
                {
                    StatusText = result.Error;
                    return;
                }

                OptiScalerRecords.Remove(GameId);
                StatusText = "Uninstalled OptiScaler.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"OptiScaler uninstall failed for '{GameName}'.");
                StatusText = $"OptiScaler uninstall failed: {ex.Message}";
                api.Dialogs.ShowErrorMessage(StatusText, "Uninstall OptiScaler");
            }

            await Task.CompletedTask;
        }
    }
}
