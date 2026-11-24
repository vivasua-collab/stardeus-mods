using Game.Mods;
using Game.Data;
using Game.Tools;
using Game.Constants;
using KL.Utils;
using KL.Signals;
using Game.Input;
using KL.Audio;
using Game.Procgen;
using Game.Procgen.Names;
using Game.Story.Events;
using System.IO;
using Game.UI;
using Game.Platform;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using Game.Achievements;
using Game.Migrations;
using Game.Scenarios;
using Game.Data.Space;
using KL.Text;
using Game.Systems.AI.Combat;
using KL.Clock;
using Game.Systems.UIUX;
using Game.Visuals;
using Game.Rendering;
using System.Threading;
using KL.Pool;
using UnityEngine.VFX;

namespace Game {
    /// <summary>
    /// The "God object". Contains references to all core plumbing.
    /// </summary>
    public static class The {
        private static bool isExitRigged;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitStatic() {
            var curThread = Thread.CurrentThread;
            if (curThread.Name != "Stardeus Main") {
                curThread.Name = "Stardeus Main";
            }
            Ready.Unready();
            SignalRegistry.Clear();
            SysSig = new SysSig();
            Platform = PlatformUtils.Create();
            Prefs = new Settings(true);
            BB = new Blackboard("Global");
            AchievementTracker = new();
            Sounds = new AudioClips();
            Defs = new Definitions();
            ModLoader = ModLoader.Create();
            Looks = new GenLooks();
            Bindings = new Bindings();
            AppIsExiting = false;
            Alloc = new Alloc();
            UITheme = null;
            RigExit();
        }
        private static void RigExit() {
            if (!isExitRigged) {
                isExitRigged = true;
                Application.wantsToQuit += Permadeath.OnWantsToQuit;
                Application.quitting += CleanupOnExit;
            }
        }

        public static bool AppIsExiting;

        private static void CleanupOnExit() {
            #if PRODUCTION_BUILD
            AchievementTracker.SaveLocal();
            AchievementTracker.Cleanup();
            #endif
            AppIsExiting = true;
            VirtualMouseInput.Instance?.RemoveDevice();
            Palette.Cleanup();
            CleanupAfterGame();
            Platform.OnShutdown();
        }
        #if !PRODUCTION_BUILD
        public const string SettingsFile = "Settings_editor.txt";
        #else
        public const string SettingsFile = "Settings.txt";
        #endif
        public static bool GameIsInitialized => isInitialized;
        private static bool isInitialized;
        public static ModLoader ModLoader;
        public static Definitions Defs;
        public static AudioClips Sounds;
        public static AchievementTracker AchievementTracker;
        public static UIDialogBox Dialog;
        public static GameRuntime R;
        public static UIScreenBlock ScreenBlock;
        public static Settings Prefs;
        public static GenLooks Looks;
        public static IPlatform Platform;
        public static SysSig SysSig;
        public static Blackboard BB;
        public static Bindings Bindings;
        public static Toolbox Toolbox => R.Toolbox;
        public static Alloc Alloc;
        public static UITheme UITheme;

        public static bool TestsAreRunning;
        public static void Initialize() {
            Ready.UnreadyGame();
            #if PRODUCTION_BUILD
            Bindings.LogConnectedDevices();
            #endif
            Bindings.SetDefaults();
            LoadGlobalSettings();
            isInitialized = true;
        }

        public static void PrepareForExit(GameState S) {
            ScreenBlock.SetCenterText(T.SuspendingNeuralNetwork, 0);
            ScreenBlock.SetSideText(null);
            ScreenBlock.Show(0);
            S.Clock.Pause();
            S.Clock.ForceStop(ForceStopReason.BulkOperation);
        }

        public static void PrepareForGame() {
            D.Log("Prepare for game");
            Ready.UnreadyGame();
            Ready.SetGame(false, "Preparing for new game");
            SignalRegistry.ReleaseNonSystem();
            DisposeRuntime();
            R = new GameRuntime();
            R.IsCurrent = true;
            R.State.InitializeForNewGame();
            isInitialized = true;
            UIPoolRoot.ResetCursor();
        }

        public static void CleanupAfterGame() {
            D.Log("Cleanup after game");
            Lifetime.DisposeAll();

            D.Cleanup();
            G.Cleanup();
            UIPoolRoot.ResetCursor();
            Ready.SetGame(false, "Cleanup after game");
            Migration.Reset();
            DisposeRuntime();
            UIShowing.ResetInGame();
            if (BB != null) {
                if (!BB.Get(BBKeyH.GlobalKeepBB, false)) {
                    BB.Clear();
                }
            }
            SignalRegistry.ReleaseQueryResponders();
            Registries.UIWidgets.Clear();
            // Modifier.ResetAll();
            CombatUnit.Cleanup();
            Caches.Flush();
            ParticleBudget.Reset();
            Registries.CleanupAfterGame();
            GameTextTMP.Reset();
            Scenario.Flush();
            SOAction.FlushAll();
            SOUtils.Cleanup();
            GenNamesLegacy.ResetUsedNames();
            ColonizePlanetCutscene.ResetAfterGame();
            Alloc.Validate(false);
            RenderingService.LogStats();

            SignalRegistry.ValidateListeners();
            SignalRegistry.ValidateSysListeners();

            PrefabPool.Cleanup((obj) => {
                if (obj) {
                    if (obj.TryGetComponent<VisualEffect>(out var vfx)) {
                        vfx.Stop();
                    }
                }
            });
        }

        public static void DisposeRuntime() {
            if (R == null) { return; }
            R.Sig.BeforeDisposeRuntime.Send();
            R.IsCurrent = false;
            R.Sys?.Unload();
            if (R.State != null) {
                R.State.Playtime = 0;
                R.State.SessionTime = 0;
                R.State.Clock?.Destroy();
                R.State.Cleanup();
                R.Toolbox.CleanupAfterGame();
            }
            R.Unload();
            R = null;
            EntityUtils.S = null;
            A.Cleanup();
            Scenario.Unload();
            MessagePack.SequencePool.Shared.Flush();
        }

        /// <summary>
        /// Local settings are stored per game save, in GameState.Prefs.
        /// <seealso cref="GameState.Prefs"/>
        /// </summary>
        private static void LoadGlobalSettings() {
            var settingsPath = Persist.Root;
            var fullPath = Files.CombinePath(settingsPath, SettingsFile);
            // Why do we register?
            Prefs.RegisterInt(Pref.GUIBottomSize, 64);
            Prefs.RegisterBool(Pref.GUIAutoFix, true);
            Prefs.RegisterBool(Pref.GSoftwareCursor, Video.CursorSoftDefault);
            Prefs.RegisterInt(Pref.GCancelDragDistancePixels,
                Consts.DefaultCancelDragDistancePx);
            Prefs.RegisterBool(Pref.GUIAutoScale, true);
            Prefs.RegisterBool(Pref.GUIAutoPadding, Consts.DefaultWidescreenAutoPadding);
            Prefs.RegisterBool(Pref.GSmoothDrag, Consts.DefaultSmoothDrag);
            // 0 is auto for all light map stuff
            Prefs.RegisterInt(Pref.GLightMapCellSize, 0);
            Prefs.RegisterInt(Pref.GLightMapMaxLightsPerCell, 0);
            Prefs.RegisterInt(Pref.GLightMapScale, 0);
            Prefs.RegisterFloat(Pref.GZoomSensitivity,
                Consts.DefaultZoomSensitivity);
            Prefs.RegisterFloat(Pref.GPanSensitivity,
                Consts.DefaultPanSensitivity);
            Prefs.RegisterFloat(Pref.GUIScale, Consts.DefaultUIScale);
            Prefs.RegisterFloat(Pref.GUIPadding, 0f);
            Prefs.RegisterBool(Pref.GAudioUIHoverSounds, true);
            Prefs.RegisterBool(Pref.GVoiceAlerts, true);
            Prefs.RegisterBool(Pref.GCosmeticParticles, true);
            Prefs.RegisterBool(Pref.GDisableVFX, false);
            Prefs.RegisterInt(Pref.GMaxParticles, Tunables.GlobalMaxParticles);
            Prefs.RegisterInt(Pref.GMaxParticlesPerType, Tunables.GlobalMaxParticlesPerType);
            Prefs.RegisterFloat(Pref.GUIFontScale, Consts.UIFontScaleDefault);
            Prefs.RegisterFloat(Pref.GDeadZoneMin, Consts.DefaultDeadZoneMin);
            Prefs.RegisterFloat(Pref.GDeadZoneMax, Consts.DefaultDeadZoneMax);
            Prefs.RegisterBool(Pref.GAllowOldSaves, false);
            Prefs.RegisterBool(Pref.GAutoSavePeriodic, true);
            Prefs.RegisterBool(Pref.GFocusOnItemsAfterTrade, true);
            Prefs.RegisterBool(Pref.GFocusOnEvents, true);
            Prefs.RegisterBool(Pref.GShowGridBounds, true);
            Prefs.RegisterBool(Pref.GShowBuildGuidelines, true);
            if (File.Exists(fullPath)) {
                ReadSettings(settingsPath);
            }
            Bindings.SetDeadZone(
                Prefs.GetFloat(Pref.GDeadZoneMin, PrefH.GDeadZoneMin, Consts.DefaultDeadZoneMin),
                Prefs.GetFloat(Pref.GDeadZoneMax, PrefH.GDeadZoneMax, Consts.DefaultDeadZoneMax));
        }
        private static void ReadSettings(string settingsPath) {
            var res = Persist.Read(settingsPath, SettingsFile,
                out var settingsSerialized);
            if (res.IsSuccess) {
                Prefs.DeserializeFromString(settingsSerialized);
                // Only with a settings file: a first launch keeps Unity's crash capture on,
                // so crashes before the consent popup still get reported
                Feedback.ApplyReportingChoice(Prefs.GetBool(Pref.GAutoReporting,
                    PrefH.GAutoReporting, false));
            } else {
                D.Err("Failed reading config json: {0}", res.FilePath);
            }
        }
        public static void SaveBindingOverrides() {
            Prefs.SetInt(Pref.GBindingsVersion, Bindings.BindingsVersion);
            foreach (var binding in Bindings.List) {
                binding.Save();
            }
        }

        public static void ApplySavedBindingOverrides() {
            var needsReset = Prefs.GetInt(Pref.GBindingsVersion,
                PrefH.GBindingsVersion, 0) != Bindings.BindingsVersion;
            var hadOverrides = false;
            foreach (var binding in Bindings.List) {
                if (binding.Load(needsReset)) {
                    hadOverrides = true;
                }
            }

            UIPoolRoot.Activator?.SetClickAction(
                Bindings.GetBinding(ActionType.Click).InputAction);

            if (needsReset && hadOverrides) {
                Ready.WhenCore(NotifyBindingsReset);
            }
            Bindings.HasAppliedOverrides = true;
            // Until now conflicts were only checked after a rebind. A saved
            // override can also collide with a default that was added later,
            // and so can the defaults of two mods.
            Ready.WhenCore(Bindings.ValidateConflicts);
        }

        private static void NotifyBindingsReset() {
            WPopup.Create(true, IconId.COperate, T.DevNotice, null,
                T.InputBindingsReset).Spawn();
        }

        public static void SaveGlobalSettings() {
            #if !PRODUCTION_BUILD
            if (TestsAreRunning) { return; }
            #endif
            D.Ass(Prefs != null, "Trying to save null settings!");
            var settingsPath = Persist.Root;
            var fullPath = Files.CombinePath(settingsPath, SettingsFile);
            var res = Persist.Write(settingsPath, SettingsFile,
                Prefs.SerializeToString(), false);
            if (!res.IsSuccess) {
                D.Err("Failed writing settings: {0}", res.FilePath);
            }
        }

        public static void End() {
            if (!isInitialized) {
                // D.Warn("Trying to unload while not initialized!");
                return;
            }
            DisposeRuntime();
            UIShowing.Reset();
            isInitialized = false;
            SaveGlobalSettings();
            RenderingService.Sprites.Unload();
            Alloc.Validate(true);
        }
    }
}
