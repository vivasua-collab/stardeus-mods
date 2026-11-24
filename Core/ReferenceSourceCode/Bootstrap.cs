#if UNITY_STANDALONE_OSX
// #define OSX_VIDEO_HACK
#endif
#if UNITY_EDITOR
// #define FORCE_PNG_ATLAS
// #define FORCE_COMPRESS_ATLAS
#endif
using UnityEngine;
using Game.Rendering;
using KL.Signals;
using Game.Constants;
using KL.Utils;
using System.Collections;
using Game.Commands;
using Game.Utils;
using Game.CodeGen;
using Game.Data;
using System.Threading;
using KL.Randomness;
using System.Globalization;
using UnityEngine.SceneManagement;
using KL.Pool;
using Game.Visuals;
using Game.UI;
using Game.Systems.Hints;
using Game.Tools;
using Game.Data.Factions;
using Game.Platform;
using UnityEngine.CrashReportHandler;
using Unity.Collections.LowLevel.Unsafe;
using Game.Systems.UIUX;
using Unity.Collections;
using Game.Data.Economy;
using System.Runtime.CompilerServices;
using System.IO;
using Game.Systems.Space;
using Game.Systems.Crafting;
using UnityEngine.VFX;
using Game.Mods;
using Game.Systems.Quests;
using System.Linq;

[assembly: InternalsVisibleTo("PlayModeTests")]
[assembly: InternalsVisibleTo("GameEditor")]
[assembly: InternalsVisibleTo("EditModeTests")]
namespace Game {
    /// <summary>
    /// Main entry point of the game. Start exploring the source code here.
    /// It also got pretty messy over the years.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class Bootstrap : MonoBehaviour {

        #region Unity Lifecycle

        private void Awake() {
            BootTimeline.Start();
            PlatformUtils.DisableWindowGhosting();
            using (BootTimeline.Span("Awake")) {
                InitializeApplication();
                AssetsManifest.StartValidation();
                SetupGraphics();
                ConfigureCulture();
                // after SetupGraphics applied the player's vsync, which End restores
                BootFramePacing.Begin();
                StartBootstrap();
            }
        }

        private void Update() {
            ProcessPendingOperations();
            ScreenWatcher.Poll();
            UpdateShaderGlobals();
            UpdateGameRuntime();
        }

        private void FixedUpdate() {
            FixedUpdateGameRuntime();
        }

        private void OnDestroy() {
            // SceneManager.activeSceneChanged -= OnSceneChanged;
            The.End();
        }

        private void OnApplicationQuit() {
            var s = A.S;
            if (PlayStats.IsEnabled && GameState.IsCurrent(s) && s.IsLoaded
                    && QuestUtils.TryGetTutorialProgress(s, out var questId, out var stepId)) {
                PlayStats.TrackTutorialAtExit(questId, stepId);
            }
            PlayStats.TrackGameExited(PlatformUtils.QuitFromMenu);
        }

        #endregion

        #region Application Initialization

        private void InitializeApplication() {
            Application.runInBackground = true;
            Application.lowMemory += OnLowMemory;
            A.MainThread = Thread.CurrentThread;

            #if PRODUCTION_BUILD
            D.Warn("Stardeus version: {0}", Consts.FullVersion);
            #endif
            Time.maximumDeltaTime = 0.05f;
            Time.maximumParticleDeltaTime = 0.05f;

            #if UNITY_EDITOR
            The.Prefs.SetBool(Pref.GSoftwareCursor, true);
            // The.SysSig.SetCursor.Send(UIPoolRoot.LastCursor);
            #endif

            // System.Threading.Tasks.Task.Run(() => {
            //     WPopup.Create(true, null, "Early popup test", "this is early!", "how about it?")
            //         .Spawn(center: true);
            // });

            Benchmarks.Run();
            // BootFramePacing shortens the slow budget (HasToYieldSlow) until boot ends
            FrameTimer.SetTargetFramerate(65, 1);
        }

        private void SetupGraphics() {
            ConfigureComputeShaders();
            ConfigureTextureCompression();
            ConfigureAtlasCache();
            ConfigureVideoSettings();
        }

        private void ConfigureComputeShaders() {
            Consts.HasComputeShaders = SystemInfo.supportsComputeShaders;
            Consts.IsAMD = SystemInfo.graphicsDeviceVendor == "ATI"
                || SystemInfo.graphicsDeviceName.ToLower().Contains("radeon");
        }

        private void ConfigureTextureCompression() {
            var gpuMem = SystemInfo.graphicsMemorySize;
            // LOL @ Intel HD GPU reporting the cosmic amounts of shared VRAM trying to look cool
            var isIntelGPU = SystemInfo.graphicsDeviceName.ToLower().Contains("intel");
            Consts.IsLessAwesomeGPU = isIntelGPU || gpuMem < 4000;
            Consts.IsPotato = Consts.IsLessAwesomeGPU || PlatformUtils.IsPotato();
            Consts.IsLaptop = !Consts.IsPotato && PlatformUtils.HasBattery();

            var forceCompressAtlas = isIntelGPU || gpuMem < 7000;
            if (!forceCompressAtlas) {
                forceCompressAtlas = PlatformUtils.HasCommandLineArg("--force-compress-atlas");
            }
            #if FORCE_COMPRESS_ATLAS
            forceCompressAtlas = true;
            #endif

            Res.WantsCompressedTextures = forceCompressAtlas;
            #if PRODUCTION_BUILD
            D.Warn("Will compress textures: {0} (GPU mem: {1})",
                Res.WantsCompressedTextures, gpuMem);
            #endif
        }

        private void ConfigureAtlasCache() {
            var forcePngAtlas = PlatformUtils.HasCommandLineArg("--force-png-atlas");
            #if FORCE_PNG_ATLAS
            forcePngAtlas = true;
            #endif

            // if we have over 15 gb of space or already created the raw cache,
            // we will use larger atlas cache that loads faster
            if (forcePngAtlas) {
                AtlasBuilder.UseRawAtlasData = false;
                D.Warn("Will use raw atlas texture cache: {0} (forced)",
                    AtlasBuilder.UseRawAtlasData);
            } else {
                // can be -1 on linux
                var remainingDiskSpaceGB = Files.GetFreeDiskSpaceAtPath(
                    ModLoader.GraphicsCacheDir);
                // A compressed raw cache is a quarter of the size (~1.1 GB), and the PNG cache
                // would make such a PC decode and compress every atlas on every launch
                var minFreeGB = Res.CompressesTextures ? 3 : 15;
                AtlasBuilder.UseRawAtlasData = remainingDiskSpaceGB < 0
                    || remainingDiskSpaceGB > minFreeGB || ModLoader.HasRawAtlasData;
                #if PRODUCTION_BUILD
                D.Warn("Will use raw atlas texture cache: {0} (Free disk space: {1} GB)",
                    AtlasBuilder.UseRawAtlasData, remainingDiskSpaceGB);
                #endif
            }
        }

        private void ConfigureVideoSettings() {
            #if UNITY_EDITOR
            UnsafeUtility.SetLeakDetectionMode(
                NativeLeakDetectionMode.EnabledWithStackTrace);
            //Consts.IsLessAwesomeGPU = true;
            #endif
            #if OSX_VIDEO_HACK
            Video.FixOSX();
            #endif
            Video.ApplyVsync(true);
        }

        private void ConfigureCulture() {
            // WARNING cannot use The.Prefs here yet, not loaded!
            var cul = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture =
                CultureInfo.DefaultThreadCurrentCulture =
                    Thread.CurrentThread.CurrentCulture = cul;
        }

        private void StartBootstrap() {
            Ready.SetCore(false, "Loading core");
            var threads = Mathf.Max(4, SystemInfo.processorCount - 1);
            The.Platform.SetPlayerRegion();

            // Intro shader variety
            Shader.SetGlobalVector(ShaderVars._GameView, Rng.UInsideUnitCircle()
                * Rng.URange(0f, 10000f));

            CrashWatchdog.StartBoot();
            // SceneManager.activeSceneChanged += OnSceneChanged;

            // can't run this in same frame!
            Coroutines.Start(InitServices(), this, "InitServices",
                trySameFrame: false, isSystem: true);
        }

        #endregion

        #region Services Initialization

        private IEnumerator InitServices() {
            using (BootTimeline.Span("Platform")) {
                yield return InitializePlatform();
            }
            // After the first frame, so the loading screen shows while they load
            yield return null;
            var libSpan = BootTimeline.Span("Prefab library");
            var libLoaded = ResourceLibrary.Preload();
            libSpan.Dispose();
            if (libLoaded) {
                yield return null;
            }
            using (BootTimeline.Span("Rendering")) {
                yield return InitializeRendering();
            }
            using (BootTimeline.Span("Assets manifest")) {
                yield return AssetsManifest.ValidateAssetsManifest();
            }
            using (BootTimeline.Span("Mods and content")) {
                yield return InitializeModsAndContent();
            }
            using (BootTimeline.Span("Localization")) {
                yield return InitializeLocalization();
            }
            using (BootTimeline.Span("Finalize")) {
                yield return FinalizeInitialization();
            }
            using (BootTimeline.Span("Initial save")) {
                yield return LoadInitialSave();
            }
        }

        private IEnumerator InitializePlatform() {
            Application.runInBackground = true;
            CrashWatchdog.UpdateBoot("Platform.BeforeInit");
            The.Platform.BeforeInit();
            The.Initialize();
            CrashWatchdog.UpdateBoot("Platform.AfterInit");
            The.Platform.AfterInit();
            CrashWatchdog.UpdateBoot($"Platform Player: {The.Platform.PlayerName}");
            yield break;
        }

        private IEnumerator InitializeRendering() {
            RenderingService.Initialize();
            RenderLayers.Init();
            CrashWatchdog.UpdateBoot("GameRenderer.LoadShaders");
            MapRenderer.LoadShaders();
            CrashWatchdog.UpdateBoot("The.ModLoader.Load");
            yield return null;
        }


        private IEnumerator InitializeModsAndContent() {
            D.Warn("Initializing Services");
            The.SysSig.LoadingMsg.Send("Initializing");

            // yield return BackdropVideo.WaitUntilLoaded();

            using (BootTimeline.Span("Mods")) {
                yield return The.ModLoader.Load();
            }

            using (BootTimeline.Span("UI theme")) {
                The.UITheme = UITheme.Load();
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }

            if (Tunables.FixedUPS < 10) {
                D.Err("Fixing Tunables.FixedUPS from {0} to 10", Tunables.FixedUPS);
                Tunables.FixedUPS = 10;
            }
            Time.fixedDeltaTime = 1f / Tunables.FixedUPS;

            yield return ModLoader.InitAsync(SelfInit.Stage.AfterMods);

            UIPoolRoot.LastCursor = 0;
            The.SysSig.SetCursor.Send(CursorTypeH.Cursor);

#if UNITY_STANDALONE_WIN
            // yes, some people try to run the game with 20MB disk space left...
            CheckFreeDiskSpace();
#endif
            using (BootTimeline.Span("Analytics")) {
                yield return Coroutines.WaitTask(GameAnalytics.SetupAnalytics(), "Setup Analytics");
            }

            // No PatchAll for the game itself: it has no [HarmonyPatch] classes, and scanning
            // every type of Game.dll for them only slowed the boot. Mods patch with their own
            // Harmony instances when their DLLs load.

            AfterAllModsLoadedHook();

            if (!The.ModLoader.AvailableMods.Contains("Core")) {
                D.Err("Failed to load core mod");
                WPopup.SystemWarning(null, "Game cannot start", "Core could not be loaded",
                    "To fix this, try:<br>1. Verify installed files integrity on Steam.<br>2. Completely uninstall and reinstall Stardeus.");
                yield break;
            }
        }

        private IEnumerator InitializeLocalization() {
            CrashWatchdog.UpdateBoot("Texts.Preload");

            using (BootTimeline.Span("Looks DB")) {
                The.Looks.BuildDB(RenderingService.Sprites.AllIds);
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }
            CrashWatchdog.UpdateBoot("Translations.SetLocale");
            var localeSpan = BootTimeline.Span("Locale");
            // The translations worker (ModLoader.LoadTranslations) has already parsed the
            // player's language, so SetLocale finds it current. Switching to English first,
            // as this did, would only make it parse the language again.
            var lang = The.Prefs.GetString(Pref.GLanguage, PrefH.GLanguage,
                Translations.DefaultLocale);
            if (!Translations.IsSupported(lang)) {
                D.Err("Trying to switch into language that is not loaded: {0}. Official: {1}. Community: {2}", lang,
                    string.Join(", ", Translations.SupportedLocales),
                    string.Join(", ", Translations.CommunityLocales));
                lang = Translations.DefaultLocale;
                The.Prefs.SetString(Pref.GLanguage, Translations.DefaultLocale);
            }
            if (lang == Translations.DefaultLocale) {
                Translations.SetLocale(Translations.DefaultLocale, true);
            } else {
                // D.Warn("Setting language to: {0}", lang);
                T.SetLocale(lang);
                if (!TranslationUtils.ValidateTranslations()) {
                    T.SetLocale(lang);
                }
            }
            CrashReportHandler.SetUserMetadata("i18n", lang);
            localeSpan.Dispose();
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }
            using (BootTimeline.Span("Hints")) {
                LoadHints.BuildDatabase();
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }

            Units.CreateTranslationsDependentCache();
            // After the language is known: CJK fonts only matter for CJK languages
            yield return FontFeatureWarmup.Run();
        }

        private IEnumerator FinalizeInitialization() {
            using (BootTimeline.Span("Before ready core hooks")) {
                The.SysSig.BeforeReadyCore.Send();
                Ready.RunBeforeReadyCore();
            }

            // TODO move this all into SelfInit
            yield return ModLoader.InitAsync(SelfInit.Stage.Finalize);
            // Each reads a folder of JSON. In one frame they froze the loading screen for
            // seconds on PCs where every file open is slow (an antivirus after an update)
            var knownDefs = new System.Action[] {
                AmbientLightDef.LoadKnown,
                NeedType.LoadKnown,
                SpaceEffectConfig.LoadKnown,
                POIConfig.LoadKnown,
                RegionTemplate.LoadKnown,
                SectorTemplate.LoadKnown,
                QuestDef.LoadKnown,
                TerraformType.LoadKnown,
                MatType.ResolveCompatibleStorages,
                Craftable.LoadKnown,
                FactionDef.LoadKnown,
            };
            using (BootTimeline.Span("Known defs")) {
                foreach (var load in knownDefs) {
                    load();
                    if (FrameTimer.HasToYieldSlow()) {
                        yield return null;
                    }
                }
            }

            using (BootTimeline.Span("Achievements")) {
                TerraformType.RegisterAchievements();
                The.AchievementTracker.LoadLocal();
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }

            // From here to the end runs in one frame, as it always did: whatever waits for
            // Ready.Core then sees the market items and the CoreLoaded hooks done too
            CrashWatchdog.UpdateBoot("Core Loaded");
            using (BootTimeline.Span("Ready core hooks")) {
                Ready.SetCore(true, "Loaded core");
                Ready.SetUserInput(true, "Init");
            }
            using (BootTimeline.Span("Bindings")) {
                Toolbox.BindShortcuts(The.Bindings);
                EntityActionHotkeys.Bind(The.Bindings);
                ShipComputerAppHotkeys.Bind(The.Bindings);
            }
            RestoreBackgroundChoice();

            using (BootTimeline.Span("Market items")) {
                MarketItem.LoadKnown();
            }
            using (BootTimeline.Span("Binding overrides")) {
                The.ApplySavedBindingOverrides();
            }

            CrashReportHandler.SetUserMetadata("has_compute_shaders",
                Consts.HasComputeShaders.ToString());
            if (!Consts.HasComputeShaders) {
                var gpu = $"{SystemInfo.graphicsDeviceName}";
                if (!gpu.Contains(SystemInfo.graphicsDeviceVendor)) {
                    gpu += $" [{SystemInfo.graphicsDeviceVendor}]";
                }
                WPopup.Create(true, null, T.NoComputeShaders, null,
                T.NoComputeShadersInfo_V1.F(gpu))
                    .WithAction(T.MenuExit, PlatformUtils.ExitGame)
                    .Spawn(center: true);
            }

            // You can hook into those using:
            // Ready.When(WhenH.CoreLoaded1, YourModMethod);
            using (BootTimeline.Span("Core loaded hooks")) {
                Ready.Set(WhenH.CoreLoaded1, true);
                Ready.Set(WhenH.CoreLoaded2, true);
            }

            CrashWatchdog.EndBoot();
#if PRODUCTION_BUILD
            D.Warn("Player: {0}/{1}:{2}", The.Platform.PlayerName,
                    The.Platform.PlayerHash, The.Platform.SysHash);
#endif
            if (The.Prefs.GetBool(Pref.GAutoReporting, PrefH.GAutoReporting, false)) {
                CrashReportHandler.SetUserMetadata("pname",
                    The.Platform.PlayerName);
            }
            yield break;
        }

        private IEnumerator LoadInitialSave() {
            using (BootTimeline.Span("Migrate orphan saves")) {
                SaveLoadUtils.MigrateOrphanSaves();
            }
            #if !PRODUCTION_BUILD
            if (!The.TestsAreRunning) {
                yield return LoadSave();
            } else {
                FinishBoot("tests");
                // skip the system warning below
                yield break;
            }
            #else
            yield return LoadSave();
            #endif
        }

        // Where boot ends: the main menu shows, the tests take over, or the first load
        // failed. Every later load calls it too, and then it does nothing.
        public static void FinishBoot(string reason) {
            JsonPrefetch.Stop();
            BootFramePacing.End();
            BootTimeline.Finish(reason);
        }

        public void AfterAllModsLoadedHook() {
            #if PRODUCTION_BUILD
            D.Warn("All mods loaded");
            #endif
        }

        #endregion

        #region System Event Handlers

        private void OnLowMemory() {
            D.Err("LOW MEMORY DETECTED!");
            WPopup.Create(true, null, "LOW MEMORY",
                "Your computer is running out of RAM!",
                "Try closing some applications and web browser tabs while playing.")
                    .Spawn(center: true);
        }

        #endregion

        #region Update Loop

        private void ProcessPendingOperations() {
            WPopupData.ProcessPending();
            The.ModLoader.CheckErrors();
            RenderUtils.UpdateResolution();
            Ready.FlushOnMainThread();
        }

        private void UpdateShaderGlobals() {
            Shader.SetGlobalFloat(ShaderVars._UnscaledTime,
                GameTimer.unscaledTime);
            Shader.SetGlobalFloat(ShaderVars._UnscaledSinTime,
                Mathf.Sin(GameTimer.unscaledTime));
            AmbientMotion.UpdateShaderTime(GameTimer.deltaTime);
        }

        private void UpdateGameRuntime() {
            var r = The.R;
            if (r == null) { return; }
            r.Sig.EveryFrame.Send();
            if (!IsReady(r)) { return; }

            var S = r.State;
            if (Ready.StateLock) {
                // Max 10% of frame budget for shit updates
                S.ActQ.Execute(GameTimer.deltaTime * 0.1f);
                S.Clock.Update(Time.deltaTime);
            }
            var dt = GameTimer.unscaledDeltaTime;
            S.Playtime += dt;
            S.SessionTime += dt;
        }

        private void FixedUpdateGameRuntime() {
            var r = The.R;
            if (r == null) { return; }
            if (!IsReady(r)) { return; }

            if (Ready.StateLock) {
                var S = r.State;
                if (!S.Clock.IsUnlimited) {
                    r.State.ActQ.Execute(GameTimer.deltaTime * 0.1f);
                }
                r.State.Clock.FixedUpdate(Time.fixedDeltaTime);
            }
        }

        #endregion

        #region Utility Methods

        public static void RestoreBackgroundChoice() {
            Application.runInBackground = The.Prefs.GetBool(
                Pref.GRunInBG, PrefH.GRunInBG, Consts.DefaultRunInBG);
        }

        private bool IsReady(GameRuntime r) {
            #if STARDEUS_DEMO
            #else
            return Ready.Game && r.IsCurrent;
            #endif
        }

        private static IEnumerator LoadSave() {
            GameStateMeta mainMenuSave = null;
            if (The.Prefs.GetBool(Pref.GLoadLastSave, PrefH.GLoadLastSave, true)) {
                The.SysSig.LoadingMsg.Send("Reading save files");
                yield return null;
                var indexSpan = BootTimeline.Span("Save index");
                yield return SaveLoadUtils.IndexSavesAsync();
                var allSaves = SaveLoadUtils.SavesByGame;
                indexSpan.Dispose();
                if (allSaves.Count > 0) {
                    // A lost No Reload colony keeps its save, but without a ship computer
                    // it cannot be the live scenery behind the menu. A placeholder meta has
                    // the file's real write time, so an unreadable save could be the newest:
                    // every boot then failed to load it and fell back to the demo save.
                    var lastLoadedSave = allSaves.Values.Select(saves => saves[0])
                        .FirstOrDefault(save => !save.IsGameOver && !save.IsPlaceholder);
                    if (lastLoadedSave != null
                            && lastLoadedSave.SaveVersion == Consts.CurrentSaveVersion
                            && !SaveLoadValidation.IsOldGameVersion(lastLoadedSave)) {
                        mainMenuSave = lastLoadedSave;
                    }
                }
            }
            The.SysSig.AdvanceLoadingStage.Send();
            yield return null;
            G.IsDemoMode = mainMenuSave == null;
            var loadCmd = new CmdLoadGame(mainMenuSave, showMainMenu: true);
            yield return loadCmd.ExecuteAsync();
            if (!loadCmd.IsSuccess) {
                FinishBoot("load failed");
                D.Warn("Load failed, will reload");
                The.Prefs.SetBool(Pref.GLoadLastSave, false);
                The.Prefs.SetBool(Pref.GDisableDemoMode, false);
                PlatformUtils.SuggestRestart(true);
                yield break;
            }
            The.SysSig.PlayUISound.Send("UI/MainMenuStart", 0.25f);
        }

        private static void CheckFreeDiskSpace() {
            float freeSpaceGB = Files.GetFreeDiskSpaceAtPath(Persist.Root);
            if (freeSpaceGB < 0.5f) {
                if (freeSpaceGB > -1f) {
                    WPopup.Create(true, IconId.CDisk, T.Warning, Texts.Red("LOW DISK SPACE!"),
                        string.Format(
                        "You have <b>{0:0}MB</b> of free disk space in <b>{1}</b>\n\nIt is recommended to exit the game now and free some disk space.",
                        freeSpaceGB * 1024, Path.GetPathRoot(Persist.Root)))
                            .Spawn(true, UIPoolRoot.SystemPopupContainer);
                }
                D.Err("Free space: {0:0.0}GB!", freeSpaceGB);
            }
        }

        #endregion

        #region Game Initialization Handlers

        private void StartNewGame() {
            // The.R.CmdQ.Enqueue(new CmdNewGame());
        }

        private void LoadGame() {
            Ready.SetGame(false, "Loading game");
            var gameSave = The.BB.Get(BBKeyH.GlobalSaveName, "default");
            // The.R.CmdQ.Enqueue(new CmdLoadGame(gameSave));
        }

        #endregion
    }
}
