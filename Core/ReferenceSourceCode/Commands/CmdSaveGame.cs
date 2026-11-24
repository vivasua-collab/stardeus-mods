#if !PRODUCTION_BUILD
// #define SAVE_LOAD_SYNC
// #define VERBOSE_SAVE
// #define DEBUG_SAVE_JSON
#endif
using Game.Constants;
using KL.Clock;
using Game.Commands;
using KL.Signals;
using KL.Utils;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MessagePack;
using Game.Utils;
using Game.CodeGen;
using Game.Data;
using Game.UI;
using System;
using Game.Rendering;
using System.IO;
using KL.Randomness;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Game.Systems.UIUX;

namespace Game.Commands {
    public class CmdSaveGame : AsyncCommand {
        [SelfInit]
        private static void ClearStatics() {
            IsRunningNow = null;
        }

        public static CmdSaveGame IsRunningNow;
        public bool IsCompleted;
        public GameStateMeta SavedMeta;
        public override bool TrySameFrame => false;
        private string fileName;
        private string savePath;
        private Persist.PersistResult result;
        public Persist.PersistResult Result => result;
        private bool isAutoSave;
        private bool skipBackup;
        private bool willExit;
        private bool isInGameSaveDir;
        public double StartTime;
        private void ReloadCacheIfNecessary() {
            if (!S.WasSaved) {
                S.WasSaved = true;
                SaveLoadUtils.ClearCache();
            }
        }
        public CmdSaveGame(GameState s, string file, bool auto, bool willExit)
                : base("save_game", system: true) {
            D.Ass(s != null, "State is not null");
            S = s;
            if (G.IsDemoMode) {
                D.Throw("Saving in demo mode!");
            }
            isAutoSave = auto;
            if (s.IsPermadeath) {
                skipBackup = true;
                isAutoSave = false;
            }
            this.willExit = willExit;
            if (isAutoSave) {
                var date = DateTime.Now.ToString(Consts.DateTimeFormatAutoSave);
                fileName = $"{Consts.AutoSavePrefix}{date}";
            } else {
                fileName = file;
            }
            SetSavePath();
        }
        public CmdSaveGame WithSaveFilePath(string path) {
            savePath = path;
            isInGameSaveDir = false;
            return this;
        }
        public System.Exception exception;
        private bool skipMeta;

        public void ExecuteSync(bool takeScreenshot) {
            if (A.IsSaveTemplate) {
                D.Err("Refusing to save a template!");
                return;
            }
            S.BeforeSave(willExit);
            SaveGame();
            if (exception != null) {
                D.Err("Failed saving sync! {0}", exception);
            }
            if (!Result.IsSuccess) {
                return;
            }
            if (takeScreenshot) {
                if (!isAutoSave && UIShowing.MainMenu) {
                    A.S.UIQuery.CloseMainMenu.Ask(true);
                }
                TakeScreenshotSync(false);
            }
            IsCompleted = true;
        }

        public void ExecuteSaveInTest() {
            if (A.IsSaveTemplate) {
                D.Err("Refusing to save a template!");
                return;
            }
            S.BeforeSave(willExit);
            skipMeta = true;
            SaveGame();
            if (exception != null) {
                D.LogEx(exception, "Exception while saving in test!");
            }
        }

        /// <summary>
        /// For bug reports
        /// </summary>
        public static string LastAutoSavePath;
        public static string PreLastAutoSavePath;

        private void SetSavePath() {
            if (fileName == "_feedback") {
                savePath = SaveLoadUtils.SaveDir(fileName);
            } else if (fileName == "_shared/_shared") {
                savePath = SaveLoadUtils.SaveDir(fileName);
                skipBackup = true;
            // to migrate the demo save into main saves
            } else if (S.SaveDir.Contains("/DemoSaves/")) {
                savePath = SaveLoadUtils.SaveDir(fileName);
                skipBackup = true;
            } else {
                savePath = Files.CombinePath(S.SaveDir, fileName);
                isInGameSaveDir = true;
            }
            if (isAutoSave) {
                PreLastAutoSavePath = LastAutoSavePath;
                LastAutoSavePath = savePath;
            }
        }

        public override IEnumerator ExecuteAsync() {
            if (A.IsSaveTemplate) {
                D.Err("Refusing to save a template!");
                yield break;
            }
            StartTime = Time.realtimeSinceStartupAsDouble;
            if (IsRunningNow != null) {
                if (isAutoSave) { yield break; }
                var startWait = Time.realtimeSinceStartupAsDouble;
                while (IsRunningNow != null) {
                    if (Time.realtimeSinceStartupAsDouble - startWait > 15d) {
                        D.Err("Waiting for save that is running for over 15 seconds! Aborting wait {0} vs {1}",
                            IsRunningNow, this);

                        if (IsRunningNow?.StartTime < Time.realtimeSinceStartupAsDouble - 30d) {
                            D.Err("Removing previously running save coroutine that was running for 30 seconds!");
                            IsRunningNow = null;
                        } else {
                            yield break;
                        }
                    }
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
            }
            if (!Ready.Save) {
                D.Err("Trying to save when save/load is running!");
                yield break;
            }
            if (!Ready.Gen) {
                if (!isAutoSave) {
                    WPopup.SystemWarning(T.SaveError, T.CannotSaveGen);
                }
                yield break;
            }
            if (S?.IsInconsistent == true) {
                if (!isAutoSave) {
                    WPopup.Create(true, IconId.CWarning, T.SaveError, null,
                        T.InconsistentGameState)
                        .WithAction(T.Ok, null)
                        .WithAction2(T.ReadyToRetry, () => {
                            S.IsInconsistent = false;
                            AbortSave();
                    }).Spawn();
                }
                D.Err("Refusing to save inconsistent game state! {0}", fileName);
                yield break;
            }
            IsRunningNow = this;
            int randHash = Rng.UInt;
            yield return Ready.WaitForSaveIO(this);
            if (!GameState.IsCurrent(S)) {
                IsRunningNow = null;
                yield break;
            }

            var clockSpeed = G.ClockSpeed;
            #if VERBOSE_SAVE
            D.Warn("Save process started: #{0}", randHash);
            #else
            D.Warn("Save process started: #{0}", randHash);
            #endif
            S.Clock.ForceStop(ForceStopReason.Save);
            if (G.ClockSpeed == ClockSpeed.Stopped) {
                S.Sig.TogglePauseFrame.Send(false);
            }
            //S.Clock.SetSpeed(ClockSpeed.Stopped);
            if (!Ready.Save) {
                D.Err("Trying to save when save/load is running (second check)!");
                // the clock was force-stopped above: a bare return left it stopped
                AbortSave();
                if (G.ClockSpeed == ClockSpeed.Stopped) {
                    S.Sig.TogglePauseFrame.Send(true);
                }
                yield break;
            }
            // Flushed here, while StateLock still allows it: SaveGame's own Flush runs on the
            // worker after StateLock went false, refused, and component data of entities
            // removed in the last tick went into the save
            S.Flush();
            Ready.SetSave(false, "Saving game");
            Ready.SetSaveIO(false, fileName);
            Ready.SetStateLock(false, "Saving");
            S.BeforeSave(willExit);
            if (S.IsInconsistent) {
                WPopup.Create(true, IconId.CWarning, T.SaveError, T.InconsistentGameState).Spawn();
                D.Err("Refusing to save inconsistent game state! {0}", fileName);
                AbortSave();
                yield break;
            }
            yield return TakeScreenshot();
            if (!GameState.IsCurrent(S)) {
                AbortSave();
                yield break;
            }
            if (willExit) {
                The.ScreenBlock.Show();
                The.ScreenBlock.SetCenterText(T.SuspendingNeuralNetwork, 0);
                The.ScreenBlock.SetSideText(T.Autosaving);
            }
            var start = Time.realtimeSinceStartupAsDouble;
            #if SAVE_LOAD_SYNC
            D.Err("Restore async save");
            SaveGame();
            #else
            var task = Task.Run(SaveGame);
            while (!task.IsCompleted) {
                yield return null;
            }
            if (!GameState.IsCurrent(S)) {
                AbortSave();
                yield break;
            }
            #endif
            // D.Warn("End of save!");
            var end = Time.realtimeSinceStartupAsDouble;
            #if PRODUCTION_BUILD
            D.Warn("Save write took {0:0.00}s", end - start);
            #endif
            if (!Ready.StateLock) {
                Ready.SetStateLock(true, "Failover after save");
            }
            if (!Ready.Save) {
                Ready.SetSave(true, "Failover after save");
            }
            if (!Ready.SaveIO) {
                Ready.SetSaveIO(true, "Failover after save");
            }
            if (G.ClockSpeed == ClockSpeed.Stopped) {
                S.Sig.TogglePauseFrame.Send(true);
            }
            if (exception != null || !result.IsSuccess) {
                if (exception != null) {
                    D.LogEx(exception, "Failed saving game");
                }
                D.Err("Failed writing save file: {0} {1}", result.FilePath,
                    result.Exception);
                WPopup.Create(true, IconId.CWarning, T.SaveFailed, null,
                    string.Format("Failed to write save file: \n<b>{0}</b>\nReason: <b>{1}</b>\n\nIf Auto Save is enabled, a save with recent progress will be available.\n\nTo prevent save file corruption, auto saving has been temporarily disabled.\n\nIf crash reporting is enabled in settings, developer was notified, but you can help by filling out the <b>Report a Bug</b> form using the buttin in the bottom right.\n\nClick <b>{2}</b> to re-enable saving. If you don't want to risk saving an inconsistent state, restart the game and load the last available save file.",
                        result.FilePath, result.Exception, T.ReadyToRetry))
                        .WithAction(T.Ok, null)
                        .WithAction2(T.ReadyToRetry, () => {
                            if (S != null) {
                                S.IsInconsistent = false;
                                AbortSave();
                            }
                    }).Spawn();
                yield return new WaitForSecondsRealtime(5);
            }
            if (!willExit) {
                int step = 1;
                try {
                    //SaveLoadUtils.ClearCache();
                    The.SysSig.GameSavesChanged.Send();
                    step = 2;
                    if (S.Clock != null) {
                        S.Clock.ForceResume(ForceStopReason.Save);
                    }
                    //S.Clock.SetSpeed(clockSpeed);
                    step = 3;
                    ReloadCacheIfNecessary();
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed post cmd save game at step {0}", step);
                }
            }
            #if VERBOSE_SAVE
            D.Warn("Save process ended: #{0}", randHash);
            #else
            D.Warn("Save process ended: #{0}", randHash);
            #endif
            IsRunningNow = null;
            IsCompleted = true;
        }

        public static void AbortSave() {
            IsRunningNow = null;
            if (!Ready.StateLock) {
                Ready.SetStateLock(true, "Failover after save");
            }
            if (!Ready.Save) {
                Ready.SetSave(true, "Failover after save");
            }
            if (!Ready.SaveIO) {
                Ready.SetSaveIO(true, "Failover after save");
            }
            if (The.R?.State?.Clock != null) {
                The.R.State.Clock.ForceResume(ForceStopReason.Save);
            }
        }

        public IEnumerator TakeScreenshot() {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!GameState.IsCurrent(S)) { yield break; }
            if (!isAutoSave && UIShowing.MainMenu) {
                A.S.UIQuery.CloseMainMenu.Ask(true);
                yield return null;
                yield return new WaitForEndOfFrame();
            }
            TakeScreenshotSync(true);
        }

        // TODO use async gpu readback to avoid frame drop
        private void TakeScreenshotSync(bool resize) {
            try {
                Files.EnsureDir(S.SaveDir);
                var tex2 = RenderUtils.CaptureScreenshotAsTexture();

                // Calculate target dimensions (FHD max: 1920x1080)
                int targetW, targetH;
                if (resize || tex2.width > 1920 || tex2.height > 1080) {
                    // Downsize to FHD while maintaining aspect ratio
                    float aspectRatio = (float)tex2.width / tex2.height;
                    if (aspectRatio > 1920f / 1080f) {
                        // Width is the limiting factor
                        targetW = Mathf.Min(tex2.width, 1920);
                        targetH = Mathf.FloorToInt(targetW / aspectRatio);
                    } else {
                        // Height is the limiting factor
                        targetH = Mathf.Min(tex2.height, 1080);
                        targetW = Mathf.FloorToInt(targetH * aspectRatio);
                    }
                } else {
                    // No resize needed
                    targetW = tex2.width;
                    targetH = tex2.height;
                }

                Texture2D finalTex;
                if (targetW != tex2.width || targetH != tex2.height) {
                    // Prepare for scaling
                    var rt = RenderTexture.GetTemporary(targetW, targetH, 24);
                    rt.name = "SaveGameTakeScreenshotSync";
                    RenderTexture.active = rt;
                    // Scale
                    Graphics.Blit(tex2, rt);
                    // Read scale result
                    finalTex = new Texture2D(targetW, targetH);
                    The.Alloc.Register(finalTex);
                    finalTex.name = "SaveScreenshot";
                    finalTex.ReadPixels(new(0, 0, targetW, targetH), 0, 0);
                    finalTex.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                } else {
                    finalTex = tex2;
                }

                // Save with JPEG compression (quality 85 for good balance)
                Persist.Write(savePath + ".jpg", finalTex.EncodeToJPG(85));

                The.Alloc.UnregisterAndDispose(tex2);
                if (finalTex != tex2) {
                    The.Alloc.UnregisterAndDispose(finalTex);
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed capturing screenshot: {0}.jpg", savePath);
            }
        }

        private void SaveGame() {
            exception = null;
            bool wasSaving = false;
            #if VERBOSE_SAVE
            D.Warn("Started saving {0}", savePath);
            #endif
            try {
                if (S.IsSavingNow) {
                    throw new Exception("Saving already in progress");
                }
                S.Flush();
                S.UpdateGameVersions();
                wasSaving = true;
                S.IsSavingNow = true;
                Files.EnsureDir(S.SaveDir);
                S.SaveTime = DateTime.UtcNow;
                var data = SaveLoadUtils.Serialize(S.GetData());
                #if DEBUG_SAVE_JSON
                var json = MessagePackSerializer.SerializeToJson(S.GetData());
                var r = Persist.Write(savePath + ".json", json, compress: false);
                D.Warn("Wrote save json: {0}", r.FilePath);
                Ready.WhenMainThread(this, "Open json", () => {
                    Files.OpenOSFolder(Path.GetDirectoryName(r.FilePath));
                });
                #endif
                var metaUn = S.GetMeta();
                Ready.SetSave(true, "Done dumping save");
                Ready.SetStateLock(true, "Done writing save");
                metaUn.SaveTime = DateTime.UtcNow.ToFileTimeUtc();
                metaUn.IsAutoSave = isAutoSave;
                if (!skipMeta) {
                    SaveMeta(savePath, metaUn);
                }
                SavedMeta = metaUn;
                // #if UNITY_EDITOR
                // if (Rng.UChance(0.5f)) {
                //     throw new NullReferenceException();
                // }
                // #endif
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    // Skip .backup and compression for speed
                    result = Persist.Write(savePath + ".save", data);
                } else {
                    result = WriteSaveWithBackup(data);
                }
                #else
                result = WriteSaveWithBackup(data);
                #endif
                if (isAutoSave) {
                    LastAutoSavePath = result.FilePath;
                    RotateAutoSaves();
                } else if (isInGameSaveDir && result.IsSuccess) {
                    // The player saved by hand, so the game exists on disk again
                    S.AutoSaveSuspended = false;
                }
                if (willExit && result.IsSuccess) {
                    S.IsSavedForExit = true;
                }
                Ready.SetSaveIO(true, "Done writing save file");
            } catch (System.Exception ex) {
                exception = ex;
                result = new Persist.PersistResult();
                result.FilePath = savePath + ".save";
                result.Exception = ex.Message;
                result.IsSuccess = false;
                Ready.SetSave(true, "Done dumping save");
                Ready.SetSaveIO(true, "Done writing save file");
            } finally {
                #if VERBOSE_SAVE
                D.Warn("Ended saving {0}", savePath);
                #endif
                if (wasSaving) {
                    S.IsSavingNow = false;
                }
            }
        }

        private Persist.PersistResult WriteSaveWithBackup(byte[] data) {
            if (skipBackup) {
                result = Persist.Write(savePath + ".save", Zip.Compress(data));
            } else {
                result = Persist.WriteSafe(savePath + ".save", Zip.Compress(data));
                AddSaveBackup();
            }
            return result;
        }

        private readonly Dictionary<FileInfo, DateTime> autoSaves = new();

        private void RotateAutoSaves() {
            autoSaves.Clear();
            // read all files in save directory, filter all auto save files,
            // and delete any that are older than max save files
            var files = Files.ListDirectoryFiles(S.SaveDir, ".save");

            foreach (var file in files) {
                if (file.Name.StartsWith(Consts.AutoSavePrefix)) {
                    if (TryDateFromFileName(file.Name, out var date)) {
                        autoSaves.Add(file, date);
                    }
                }
            }
            var maxAutoSaves = Tunables.AutoSaveCount;
            if (maxAutoSaves <= 0) {
                maxAutoSaves = 0;
            } else if (S.IsPermadeath) {
                maxAutoSaves = 1;
            }
            if (autoSaves.Count <= maxAutoSaves) {
                autoSaves.Clear();
                return;
            }

            // Logging can break the saving, don't do it!
            // D.Log("Keeping {0} autosaves", maxAutoSaves);

            var toDelete = autoSaves.OrderByDescending(fd => fd.Value)
                .Skip(maxAutoSaves)
                .Select(fd => fd.Key).ToArray();
            foreach (var file in toDelete) {
                // D.Warn("deleting: {0}", file.Name);
                file.Delete();
                var fileWOExt = Files.RemoveExtension(file.Name);
                var img = $"{S.SaveDir}/{fileWOExt}.jpg";
                if (File.Exists(img)) { File.Delete(img); }
                var meta = $"{S.SaveDir}/{fileWOExt}.meta";
                if (File.Exists(meta)) { File.Delete(meta); }
                SaveBackupCleanup.DeleteBackupsFor($"{S.SaveDir}/{fileWOExt}");
            }
            autoSaves.Clear();
        }

        private static bool TryDateFromFileName(string fileName, out DateTime date) {
            try {
                fileName = Files.RemoveExtension(fileName);
                var datePart = fileName.Substring(Consts.AutoSavePrefix.Length);
                // D.Warn("DatePart: {0}", datePart);
                date = DateTime.ParseExact(datePart, Consts.DateTimeFormatAutoSave,
                        CultureInfo.InvariantCulture);
                return true;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed parsing date from file name: {0}", fileName);
                date = default;
                return false;
            }
        }

        private void AddSaveBackup() {
            if (isAutoSave || S.IsPermadeath || !result.IsSuccess) {
                return;
            }
            // Make a copy of the backup for recovery
            var backup = savePath + ".save.backup";
            try {
                if (File.Exists(backup)) {
                    var backupCopy = $"{backup}2";
                    File.Copy(backup, backupCopy, true);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed making a copy of the backup: {0}", backup);
            }
        }

        public static void SaveMeta(string savePath, GameStateMeta metaUn) {
            try {
                var meta = SaveLoadUtils.Serialize(metaUn);
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    D.Warn("Would write {0}.meta but won't because Unity will get confused", savePath);
                    return;
                }
                #endif
                if (!savePath.EndsWith(".meta")) {
                    savePath = $"{savePath}.meta";
                }
                Persist.Write(savePath, Zip.Compress(meta));
            } catch (Exception ex) {
                D.LogEx(ex, "Failed saving meta: {0}", savePath);
            }
        }
    }
}
