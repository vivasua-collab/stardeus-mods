#if !PRODUCTION_BUILD
// #define SAVE_LOAD_SYNC
#endif
// Comment this out when debugging issues that happen right after load
#define WARMUP_AFTER_LOAD

using Game.Constants;
using Game.Commands;
using KL.Signals;
using KL.Utils;
using System.Collections;
using System.Threading.Tasks;
using System;
using Game.Data;
using UnityEngine;
using MessagePack;
using System.Text;
using Game.Utils;
using Game.CodeGen;
using UnityEngine.SceneManagement;
using Game.UI;
using Game.Migrations;
using Game.Mods;
using Game.Audio;
using System.IO;
using Game.Systems.UIUX;

namespace Game.Commands {
    public class CmdLoadGame : AsyncCommand {
        #if PRODUCTION_BUILD
        public override bool TrySameFrame => false;
        #else
        public override bool TrySameFrame => The.TestsAreRunning;
        #endif
        private readonly GameStateMeta meta;
        private GameStateData newState;
        private bool loadSuccessful;
        private bool showMainMenu;
        private string exceptionMsg;
        private bool isDemo;
        // Measured on the worker that reads the save, recorded by the main thread after it
        private double decodeSec;
        public bool IsSuccess;
        private Action afterLoad;
        [ClearOnReload]
        private static bool isLoadingNow;
        #if !PRODUCTION_BUILD
        // Prevent one test failure from cascading further
        public static void CleanupForTests() {
            isLoadingNow = false;
        }
        #endif
        string saveFile;

        public CmdLoadGame(GameStateMeta meta, bool showMainMenu, Action afterLoad = null) : base("load_game", system: true) {
            this.meta = meta;
            isDemo = meta == null;
            this.showMainMenu = showMainMenu;
            this.afterLoad = afterLoad;
            if (isLoadingNow) {
                D.Throw("Creating CmdLoadGame while another save is still loading!");
            }
        }
        private void AdvanceStage() {
            The.SysSig.AdvanceLoadingStage.Send();
        }
        public override IEnumerator ExecuteAsync() {
            if (isLoadingNow) {
                D.Throw("Another save is loading!");
            }
            isLoadingNow = true;
            Application.runInBackground = true;
            var curScene = SceneManager.GetActiveScene();
            if (curScene.name == "Game") {
                yield return null;
                // Cleanup in case we quit with material being set
                FullScreenPassVFXRendererFeature.RemovePass();

                The.CleanupAfterGame();
                yield return MiscUtils.LoadSceneTimedAsync("Empty");
            } else {
                yield return null;
            }
            AdvanceStage();

            The.PrepareForGame();
            G.IsDemoMode = isDemo;
            S = The.R.State;
            Ready.SetSave(false, "Loading game");
            yield return Ready.WaitForSaveIO(this);
            using (BootTimeline.Span("Scene load")) {
                yield return MiscUtils.LoadSceneTimedAsync("Game");
            }
            // The runtime was disposed while this waited (a test's TearDown under a game over's
            // demo load): reading the save into the dead state threw on its cleared fields
            if (!GameState.IsCurrent(S)) {
                isLoadingNow = false;
                yield break;
            }
            AdvanceStage();
            // loading screen will be above it anyway
            The.ScreenBlock.HideImmediate(showBlockedCanvas: false);
            // Reset the cursor
            UIPoolRoot.LastCursor = 0;
            The.SysSig.SetCursor.Send(CursorTypeH.Cursor);

            The.SysSig.LoadingMsg.Send("Reading and deserializing save file");
            S.BeforeLoad();

            if (TrySameFrame) {
                LoadGame();
            } else {
                #if SAVE_LOAD_SYNC
                // Sync
                LoadGame();
                #else
                // Async
                var task = Task.Run(LoadGame);
                var decodeSpan = BootTimeline.Span("Save decode wait");
                while (!task.IsCompleted) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) {
                        decodeSpan.Dispose();
                        isLoadingNow = false;
                        yield break;
                    }
                }
                decodeSpan.Dispose();
                #endif
            }
            BootTimeline.Record("Save decode (worker)", decodeSec);
            AdvanceStage();
            if (loadSuccessful) {
                // A throw here used to kill this coroutine with isLoadingNow still set: the
                // loading screen stayed up and every later load threw until a restart. It now
                // takes the inconsistent-state path below, which falls back to the demo save.
                var restored = false;
                var restoreSpan = BootTimeline.Span("Load systems");
                try {
                    The.SysSig.LoadingMsg.Send("Initializing game state");
                    S.Sys.Initialize(The.R, S.GameVersions.SafeGet(0));
                    The.SysSig.LoadingMsg.Send("Validating save file");
                    Migration.BeforeSysRestore(S, newState);
                    The.SysSig.LoadingMsg.Send("Restoring systems");
                    S.Sys.Restore(newState);
                    restored = true;
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed restoring the loaded game state");
                    S.IsInconsistent = true;
                }
                restoreSpan.Dispose();
                newState = null;
                if (restored) {
                    The.SysSig.LoadingMsg.Send("Finalizing load");
                    using (BootTimeline.Span("Load after load")) {
                        yield return S.AfterLoad();
                    }
                    AdvanceStage();
                    TouchMeta();
                    // The restore and the game going live used to share one frame, the
                    // longest of the load. In between, the state is complete and only the
                    // game does not run yet, like on any frame before Ready.Game.
                    yield return null;
                    if (!GameState.IsCurrent(S)) {
                        isLoadingNow = false;
                        yield break;
                    }
                }
            } else {
                D.FIXME("Recover from load error");
                The.SysSig.LoadingMsg.Send("Load error!");
            }
            using (BootTimeline.Span("Load ready")) {
                try {
                    Ready.SetSave(true, "Done loading game");
                    Ready.SetGame(true, "Done loading game");
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failure after loading");
                    S.IsInconsistent = true;
                }
            }
            if (S.IsInconsistent) {
                isLoadingNow = false;
                if (isDemo) {
                    // The fallback itself failed. Loading it again fails the same way: a mod
                    // that broke the demo save used to keep boot loading it forever
                    The.SysSig.LoadingMsg.Send("Loading failed");
                    Bootstrap.FinishBoot("demo load failed");
                    DemoLoadFailurePopup.Show();
                    yield break;
                }
                The.SysSig.LoadingMsg.Send("Loading failed, trying demo save");
                var demoLoad = new CmdLoadGame(null, showMainMenu: true,
                    afterLoad: afterLoad);
                yield return demoLoad.ExecuteAsync();
                IsSuccess = demoLoad.IsSuccess;
                yield break;
            }

            try {
                The.SysSig.LoadingMsg.Send("Preparing graphics");
                using (BootTimeline.Span("Load tile graphics")) {
                    S.Sig.ResetTileGraphics.Send();
                }
                The.SysSig.LoadingMsg.Send("Executing post load actions");
                S.Clock.AfterLoad();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed preparing the loaded game");
            }

            #if WARMUP_AFTER_LOAD
            #if !PRODUCTION_BUILD
            if (CachedPlayerPrefs.GetInt("SkipLoadWarmup", 0) == 0) {
            #endif
            The.SysSig.LoadingMsg.Send("Warming up");
            var warmupSpan = BootTimeline.Span("Load warmup");
            // Not in the frame that made the game live: a warmup tick is a whole tick
            yield return null;
            for (int i = 0; i < 4; i++) {
                try {
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                    S.Clock.TryForceTick();
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed post load warmup iteration {0}", i);
                }
                yield return null;
            }
            warmupSpan.Dispose();
            AdvanceStage();
            S.Clock.SetSpeed(ClockSpeed.Stopped);
            #if !PRODUCTION_BUILD
            }
            #endif
            #endif

            // Sounds.PlayMusic(SoundtrackMood.Regular, true, 0.5f);

            The.SysSig.LoadingMsg.Send("Running late save migrations");

            yield return Migration.RunOn(S, false);
            AdvanceStage();
            if (!GameState.IsCurrent(S)) {
                isLoadingNow = false;
                yield break;
            }
            // The game is already playable here, so a failure in the rest is logged and the
            // load still finishes: it used to leave isLoadingNow set and the loading screen up
            try {
                using (BootTimeline.Span("Load finish")) {
                    FinishLoad();
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed finishing the load");
                The.SysSig.ShowLoadingScreen.Send("Loading complete", false);
            } finally {
                isLoadingNow = false;
                Bootstrap.FinishBoot("main menu");
            }
            IsSuccess = true;
        }

        private void FinishLoad() {
            // S.UpdateGameVersions();
            The.SysSig.LoadingMsg.Send("Touching the save file");
            TouchSave();

            The.SysSig.LoadingMsg.Send("Priming scenario");
            S.Scenario.AfterLoad(S);

            if (meta != null && meta.IsTemplateMode) {
                S.Vars.IsTemplateMode = true;
                CloneGameUI.ShowTemplatePopup();
            }

            #if !PRODUCTION_BUILD
            //S.IsPermadeath = false;
            if (GameState.IsCurrent(S)) {
                if (!The.TestsAreRunning) {
                    D.Warn("Disabling periodic auto save in non-production build after load");
                }
                The.Prefs.SetBool(Pref.GAutoSavePeriodic, false);
                S.Prefs.SetBool(Pref.AutoSaveOnExit, false);
            }
            #endif
            #if PRODUCTION_BUILD
            if (isDemo) {
                D.Warn("Finished loading demo save: {0}", saveFile);
            } else if (meta != null) {
                D.Warn("Finished loading game save: {0} | {1}",
                    meta.GameName, meta.SaveFile);
            }
            #endif

            CrashWatchdog.CheckCrashReports();
            RestoreRunInBackgroundChoice();
            if (!showMainMenu && exceptionMsg == null) {
                S.UIQuery.ShowMainMenu.Ask(false);
            }
            The.SysSig.ShowLoadingScreen.Send("Loading complete", false);

            var disableDemoMode = The.Prefs.GetBool(Pref.GDisableDemoMode, PrefH.GDisableDemoMode, false);

            if (showMainMenu && !disableDemoMode) {
                SandboxBlock_Camera.StartDemoFlyThrough();
            }
            afterLoad?.Invoke();
            if (disableDemoMode && G.ClockSpeed > S.Clock.MinSpeed) {
                S.Clock.SetSpeed(ClockSpeed.Stopped);
            }
        }

        private void LoadGame() {
            var decodeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try {
                ReadAndApplySave();
            } finally {
                decodeSec = (System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart)
                    / (double) System.Diagnostics.Stopwatch.Frequency;
            }
        }

        private void ReadAndApplySave() {
            loadSuccessful = false;

            byte[] contents;
            Persist.PersistResult result;
            if (isDemo) {
                saveFile = SaveLoadUtils.RandomDemoSave();
                result = Persist.ReadBytes(saveFile, out contents);
            } else {
                saveFile = meta.SaveFile;
                result = Persist.ReadBytes($"{saveFile}.save", out contents);
            }
            if (!result.IsSuccess) {
                D.Err("Failed reading save file (exists: {1}): {0}", result.FilePath,
                    File.Exists(result.FilePath));
                return;
            }
            try {
                if (Arrays.IsEmpty(contents) || contents[0] == 0) {
                    D.Err("Got a save file with leading zero byte or empty: {0}! Trying to fall back to .backup",
                        saveFile);
                    result = Persist.ReadBytes($"{saveFile}.save.backup", out contents);
                    if (!result.IsSuccess) {
                        D.Err("Failed reading backup of save file: {0}", saveFile);
                        return;
                    }
                    if (Arrays.IsEmpty(contents) || contents[0] == 0) {
                        D.Err("Got a save file BACKUP with leading zero byte or empty: {0}! Giving up.",
                            saveFile);
                        return;
                    }
                    D.Warn("Backup recovered for save file: {0}", saveFile);
                    WPopup.Create(true, null, "SAVE FILE WARNING", null,
                        $"The save file <b>{saveFile}.save</b> is corrupted and failed to load. The backup was loaded instead. Some progress may have been lost.\n\nYou can use <b>Report a bug</b> button in bottom right to report this incident to the developer. Please include any important details (i.e. if you have shut down the game via Alt+F4 or killed the game process without clean exit).")
                        .Spawn(center: true);
                }
                // Can't if async!
                // contents = Zip.TryDecompress(contents);
                newState = SaveLoadUtils.Deserialize<GameStateData>(contents);
                if (A.IsSaveTemplate) {
                    newState.IsAchievementsBlocked = true;
                }

                Migration.AfterDeserializeState(S, newState);

                S.SetData(newState);
                if (string.IsNullOrWhiteSpace(S.GameName)) {
                    var gameName = The.BB.Get(BBKeyH.GlobalGameName, "Untitled");
                    S.GameName = gameName;
                }
                Files.GetDirAndName(saveFile, out var saveDir, out var saveName);

                S.SaveDir = saveDir;
                S.SaveName = saveName;
                loadSuccessful = true;
            } catch (Exception ex) {
                exceptionMsg = D.FullExceptionMsg(ex);
                D.LogEx(ex, "Failed deserializing the save file: {0}",
                    result.FilePath);
            }
        }
        private void TouchSave() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                return;
            }
            #endif
            if (A.IsSaveTemplate) { return; }
            if (!isDemo) {
                Persist.Touch($"{meta.SaveFile}.save");
            }
        }

        private void TouchMeta() {
            if (A.IsSaveTemplate || isDemo) { return; }
            var freshMeta = S.GetMeta();
            S.LoadTime = DateTime.UtcNow;
            freshMeta.LoadTime = S.LoadTime.ToFileTimeUtc();
        }
        private void RestoreRunInBackgroundChoice() {
            Application.runInBackground = The.Prefs.GetBool(
                Pref.GRunInBG, PrefH.GRunInBG, Consts.DefaultRunInBG);
        }
    }
}
