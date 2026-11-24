using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class LoadGameWidget : BaseWidget {
        [SerializeField] private WButton closeButton;
        [SerializeField] private WText gameNameText;
        [SerializeField] private WText gameNameSubText;
        [SerializeField] private WText saveFileText;
        [SerializeField] private WDropdown saveFileDropdown;
        [SerializeField] private WButtonText btnLoadGame;
        [SerializeField] private WButtonIcon btnMoreActions;
        [SerializeField] private Image screenshot;
        [SerializeField] private AutoSizeImageLayoutElement screenshotAutoSize;
        private WPopupData deleteSavePopup;
        private GameStateMeta curMeta;
        private string saveFile;
        private bool isForSaving;
        private List<GameStateMeta> saves;
        public Action OnDeleteSave;
        private Lifetime lifetime;
        private Texture2D tex;
        private void Awake() {
            // name of the panel should be loaded
            btnLoadGame.SetOnClick(SaveOrLoadSave);
            closeButton.Load(Close);
        }

        private string ShowSaveTip() {
            if (curMeta != null) {
                return SaveLoadUtils.SaveTooltip(curMeta);
            }
            return null;
        }

        private void LoadGameTemplateMode() {
            SaveLoadValidation.LoadWithValidation(curMeta,
                templateMode: true);
        }

        private void DeleteSaveWithConfirmation() {
            if (deleteSavePopup != null) {
                deleteSavePopup.Despawn();
            }
            deleteSavePopup = WPopup.Create(true, null, T.GameDeleteConfirm,
                curMeta.SaveFile.Replace(Files.ToForwardSlashes(Application.persistentDataPath), ""),
                T.GameDeleteConfirmDetails)
                    .WithAction2(T.Cancel, null)
                    .WithAction(T.Confirm, DeleteSave)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
        }

        private void DeleteSave() {
            var saveDir = curMeta.SaveDir;
            DeleteSaveFiles(curMeta);

            SaveLoadUtils.ClearCache();
            saves = LoadableSaves(curMeta);
            if (isForSaving) {
                // it will despawn itself
                deleteSavePopup = null;
                Close();
            } else {
                if (saves?.Count > 0) {
                    OnSaveChange(saves[0]);
                } else {
                    if (Files.IsDirEmpty(curMeta.SaveDir)
                            || OnlyContainsBackupFiles(curMeta.SaveDir)) {
                        Files.ClearDirectory(curMeta.SaveDir, true);
                    } else {
                        D.Err("Refusing to delete the save directory that is not empty: {0}",
                            curMeta.SaveDir);
                    }
                    // it will despawn itself
                    deleteSavePopup = null;
                    Close();
                }
            }
            OnDeleteSave?.Invoke();
            OnSavesDeleted(saveDir);
        }

        private void DeleteAllSavesWithConfirmation() {
            if (deleteSavePopup != null) {
                deleteSavePopup.Despawn();
            }
            deleteSavePopup = WPopup.Create(true, null, T.GameDeleteConfirm,
                T.DeleteAllSaves_V1.F(saves.Count), T.GameDeleteAllConfirmDetails)
                    .WithAction2(T.Cancel, null)
                    .WithAction(T.Confirm, DeleteAllSaves)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
        }

        private void DeleteAllSaves() {
            var saveDir = curMeta.SaveDir;
            Files.ClearDirectory(saveDir, true);
            SaveLoadUtils.ClearCache();
            deleteSavePopup = null;
            Close();
            OnDeleteSave?.Invoke();
            OnSavesDeleted(saveDir);
        }

        private void DeleteAllAutoSaves() {
            var selectedMeta = curMeta;
            foreach (var save in saves) {
                if (IsAutoSave(save)) {
                    DeleteSaveFiles(save);
                }
            }

            SaveLoadUtils.ClearCache();
            OnDeleteSave?.Invoke();
            saves = LoadableSaves(selectedMeta);
            if (saves?.Count > 0) {
                var nextSave = saves.Find(save => save.SaveName == selectedMeta.SaveName)
                    ?? saves[0];
                OnSaveChange(nextSave);
            } else if (SaveLoadUtils.AvailableSaves.Count > 0) {
                var nextSave = SaveLoadUtils.AvailableSaves[0];
                saves = LoadableSaves(nextSave);
                OnSaveChange(nextSave);
            } else {
                Close();
            }
            OnSavesDeleted(selectedMeta.SaveDir);
        }

        // Continue resumes the game in memory, not a file. Once the loaded game has
        // no saves left, it must not be resumable from the menu, and mid-game it
        // must not be recreated on disk by an autosave the player did not ask for
        private void OnSavesDeleted(string saveDir) {
            if (G.IsDemoMode || A.S?.SaveDir == null || saveDir == null) {
                return;
            }
            if (Files.CleanPath(A.S.SaveDir) != Files.CleanPath(saveDir)) {
                return;
            }
            if (Files.ListDirectoryFiles(saveDir, ".save").Count > 0) {
                return;
            }
            if (G.HasEnteredGame) {
                A.S.AutoSaveSuspended = true;
            } else {
                Close();
                SaveLoadUtils.ReturnToDemo();
            }
        }

        private static void DeleteSaveFiles(GameStateMeta meta) {
            File.Delete($"{meta.SaveFile}.save");
            File.Delete($"{meta.SaveFile}.meta");
            File.Delete($"{meta.SaveFile}.jpg");
            SaveBackupCleanup.DeleteBackupsFor(meta.SaveFile);
        }

        // A No Reload colony has one save. Older files of it, left by earlier versions,
        // would be a way back, so only its newest save is offered
        private static List<GameStateMeta> LoadableSaves(GameStateMeta meta) {
            var all = SaveLoadUtils.GetAllForCurrent(meta);
            if (all?.Count > 1 && all[0].IsPermadeath) {
                return new List<GameStateMeta> { all[0] };
            }
            return all;
        }

        private static bool IsAutoSave(GameStateMeta meta) {
            return meta.SaveName.StartsWith(Consts.AutoSavePrefix,
                StringComparison.Ordinal);
        }

        private static readonly Regex backupFileRegex =
            new(@"\.(save|meta)\.(backup\d*|autobackup(\.backup)?)$",
                RegexOptions.IgnoreCase);

        private static bool OnlyContainsBackupFiles(string dir) {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir)) {
                if (Directory.Exists(entry) || !backupFileRegex.IsMatch(entry)) {
                    return false;
                }
            }
            return true;
        }

        private void OpenDir() {
            if (curMeta == null || !Directory.Exists(curMeta.SaveDir)) {
                var saveDir = Persist.PersistentPath(Consts.SaveDirectory);
                Files.OpenOSFolder(saveDir);
            } else {
                Files.OpenOSFolder(curMeta.SaveDir);
            }
            UISounds.PlayPopup();
        }

        public override void Toggle(bool show) {
            if (show) {
                if (gameObject.activeSelf) {
                    return;
                }
                gameObject.SetActive(true);
                if (lifetime != null) {
                    lifetime.Dispose();
                }
                lifetime = Lifetime.Create("LoadGameWidget");
                RegisterTooltips();
            } else {
                Close();
            }
        }

        private void RegisterTooltips() {
            Tooltip.CreateFor(lifetime, this, btnLoadGame.Rect, ShowSaveTip);
        }

        public void Close() {
            gameObject.SetActiveFast(false);
            deleteSavePopup?.Despawn();
            // if (curMeta != null) {
            //     A.S.UIQuery.RemoveTooltipsFor.Ask(this);
            // }
            curMeta = null;
            saves = null;
            saveFileDropdown.Clear();
            ReleaseScreenshot();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        // The screenshot was loaded with a game running, so Alloc counts it as that game's,
        // and the game's cleanup reported it as leaking when it outlived the game: on a load
        // from this widget, and again on quit
        private void ReleaseScreenshot() {
            if (tex == null) { return; }
            screenshot.SetActiveFast(false);
            The.Alloc.UnregisterAndDispose(tex);
            tex = null;
        }

        private void SaveOrLoadSave() {
            if (isForSaving) {
                A.S.CmdQ.Enqueue(new CmdSaveGame(A.S, curMeta.SaveName, false, false));
            } else {
                ReleaseScreenshot();
                SaveLoadValidation.LoadWithValidation(curMeta);
            }
        }

        public void Load(GameStateMeta meta, bool forSave = false) {
            isForSaving = forSave;
            if (!isForSaving) {
                saves = LoadableSaves(meta);
            }
            OnSaveChange(meta);
        }
        private void OnSaveChange(GameStateMeta meta) {
            deleteSavePopup?.Despawn();
            curMeta = meta;

            if (isForSaving || saves.Count < 2) {
                saveFileText.SetActiveFast(false);
                saveFileDropdown.SetActiveFast(false);
            } else {
                saveFileText.Load($"{T.SaveFile} (x{saves.Count})");
                saveFileDropdown.Clear();
                var opts = new List<string>();
                foreach (var save in saves) {
                    opts.Add($"{save.SaveName} ({SaveLoadUtils.PlayTime(save)})");
                }
                saveFileDropdown.Load(opts, saves.IndexOf(meta), (idx) => OnSaveChange(saves[idx]));
                saveFileText.SetActiveFast(true);
                saveFileDropdown.SetActiveFast(true);
            }

            if (isForSaving) {
                btnLoadGame.SetText(T.MenuSave);
            } else {
                btnLoadGame.SetText(T.LoadSave);
            }
            var qc = QuickChoice.Create();
            qc.Widget = btnMoreActions.Rect;
            qc.AddButton(Texts.ExternalLink(T.OpenDir), OpenDir, Consts.SaveDirectory);
            if (!isForSaving) {
                qc.AddButton(T.GameNewDesc, LoadGameTemplateMode, T.GameTemplateNew);
            }
            qc.AddButton(T.DeleteSave, DeleteSaveWithConfirmation);
            if (!isForSaving) {
                int autoSaveCount = saves.FindAll(IsAutoSave).Count;
                if (saves.Count > 1) {
                    qc.AddButton(T.DeleteAllSaves_V1.F(saves.Count),
                        DeleteAllSavesWithConfirmation);
                }
                if (autoSaveCount > 0) {
                    qc.AddButton(T.DeleteAllAutosaves_V1.F(autoSaveCount),
                        DeleteAllAutoSaves);
                }
            }
            btnMoreActions.Load(IconId.CCog, qc.Show);

            saveFile = Files.CombinePath(meta.SaveDir, meta.SaveName);
            var saveExists = File.Exists($"{saveFile}.save");

            var gameName = SaveLoadUtils.SaveName(meta, out var subText);
            if (isForSaving) {
                subText = $"{gameName}\n{subText}";
                gameNameText.SetText(meta.SaveName);
            } else {
                gameNameText.SetText(gameName);
            }
            gameNameSubText.SetText(subText);

            if (!saveExists) {
                screenshot.SetActiveFast(false);
                return;
            }

            var pic = $"{saveFile}.jpg";
            if (!File.Exists(pic)) {
                screenshot.SetActiveFast(false);
            } else {
                ReleaseScreenshot();
                tex = Res.LoadTexture(pic, false, true);
                screenshot.sprite = Res.CreateSpriteFrom(tex);
                screenshot.SetActiveFast(true);
                screenshotAutoSize.Calculate();
            }
        }
    }
}
