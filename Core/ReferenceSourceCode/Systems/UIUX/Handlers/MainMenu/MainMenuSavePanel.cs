#if PRODUCTION_BUILD
#define AUTO_REFRESH_SAVES
#endif
#define AUTO_REFRESH_SAVES
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Platform.Steam;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class MainMenuSavePanel : BaseMainMenuPanel {
        override protected string Id => "Save";
        public MainMenuSavePanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.LoadGameWidget.OnDeleteSave = OnDeleteSave;
            ctx.SubMenuPanel.Toggle(true);
        }

        private void OnDeleteSave() {
            RefreshSaves();
        }

        protected override void OnLoadContents() {
            int idx = 0;
            #if AUTO_REFRESH_SAVES
            SaveLoadUtils.ClearCache();
            #endif

            var meta = A.S.GetMeta();

            GameStateMeta first = null;
            if (TryGetSavesOfThisGame(out var saves)) {
                foreach (var save in saves.Where(s => !s.IsAutoSave).OrderByDescending(s => s.Playtime).ThenBy(s => s.SaveName)) {
                    var saveBtn = UISpawn.Widget<WButtonText2>(lifetime,
                        UIPrefabId.WMainMenuButtonSub, ctx.SubMenuPanel.Content.Container, idx++);
                    RigSaveButton(saveBtn, save);
                    first ??= save;
                }
            }

            var newBtn = UISpawn.Widget<WButtonText2>(lifetime, UIPrefabId.WMainMenuButtonSub,
                ctx.SubMenuPanel.Content.Container, idx++);
            newBtn.Load(Texts.Accented(T.CreateNewSave, false),
                () => ShowSaveDialog(first), Units.SecondsToHours(meta.Playtime));

            #if !DISABLESTEAMWORKS
            if (G.HasEnteredGame) {
                var shareBtn = UISpawn.Widget<WButtonText2>(lifetime, UIPrefabId.WMainMenuButtonSub,
                    ctx.SubMenuPanel.Content.Container, idx++);
                #if STARDEUS_DEMO
                // shared ships load only in the full game
                shareBtn.Load(Texts.Grey(Texts.Strike(T.ShareShip)),
                    () => WPopup.SystemWarning(T.OperationForbidden, T.NotInDemo), "");
                UIUtils.AddTip(lifetime, shareBtn, T.NotInDemo);
                #else
                shareBtn.Load(Texts.Accented(T.ShareShip, false), OnShareClick, "");
                #endif
            }
            #endif

            #if !AUTO_REFRESH_SAVES
            var refreshBtn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WMenuPanelButton, ctx.SubMenuPanel.Content.Container, idx++);
            refreshBtn.Load(Texts.Accented(T.Refresh, false), RefreshSaves);
            #endif
            if (first != null) {
                LoadSave(first);
            }
        }

        private void RefreshSaves() {
            RefreshSaves(clearCache: true);
        }
        private void RefreshSaves(bool clearCache) {
            if (clearCache) {
                SaveLoadUtils.ClearCache();
            }
            LoadContents();
            var scroll = ctx.SubMenuPanel.GetComponentInChildren<ScrollRect>();
            if (scroll != null) {
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        private void RigSaveButton(WButtonText2 loadBtn,
                GameStateMeta meta) {
            var saveName = SaveLoadUtils.SaveName(meta, out var subText);
            saveName = meta.SaveName;
            loadBtn.SetText(saveName);
            loadBtn.SetText2(subText);//Units.SecondsToHours(meta.Playtime));
            loadBtn.SetOnClick(() => LoadSave(meta));
            Tooltip.CreateFor(lifetime, ctx.SubMenuPanel, loadBtn.Rect, () => SaveLoadUtils.SaveTooltip(meta));
        }

        private void LoadSave(GameStateMeta meta) {
            ctx.LoadGameWidget.Load(meta, forSave: true);
            ctx.LoadGameWidget.Toggle(true);
        }

        private void ShowSaveDialog(GameStateMeta meta) {
            The.SysSig.HideTooltip.Send();
            var subHeader = $"{A.S.GameName} / {Units.SecondsToHours(A.S.Playtime)}";
            var saveName = meta != null && !meta.GameName.Contains("Autosave_")
                    ? SaveLoadUtils.SaveName(meta, out _) : A.S.GameName;
            WPopup.Create(true, IconId.WSaveGame, T.MenuSaveDesc, subHeader)
                .Unique("main_menu_choice")
                .WithInput(T.SaveFile, T.EnterName, saveName, TrySave)
                // first button will also submit the input automatically
                .WithAction(T.MenuSave, null)
                .WithAction2(T.Cancel, null)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
        }
        #if !DISABLESTEAMWORKS
        private void OnShareClick() {
            Coroutines.StartDirect(CaptureAndShow());
        }

        private IEnumerator CaptureAndShow() {
            var inner = SteamSharedSave.CaptureScreenshot();
            while (inner.MoveNext()) {
                yield return inner.Current;
            }
            ctx.ShowShareSave();
        }
        #endif

        // The saves index is keyed by the game's save folder, which a ship rename does not
        // change while the game name follows the ship (STA-1914)
        private static bool TryGetSavesOfThisGame(out List<GameStateMeta> saves) {
            return SaveLoadUtils.SavesByGame.TryGetValue(
                SaveLoadUtils.SaveFolderName(A.S.SaveDir), out saves);
        }

        private string TrySave(string name) {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                return T.EnterName;
            }
            if (TryGetSavesOfThisGame(out var saves)) {
                foreach (var save in saves) {
                    if (save.SaveName == name) {
                        return T.ObjectAlreadyExists_V1.F(T.SaveFile);
                    }
                }
            }
            name = Files.SanitizeFileName(name);
            A.S.SaveName = name;
            A.S.CmdQ.Enqueue(new CmdSaveGame(A.S, name, false, false));
            return null;
        }
    }
}
