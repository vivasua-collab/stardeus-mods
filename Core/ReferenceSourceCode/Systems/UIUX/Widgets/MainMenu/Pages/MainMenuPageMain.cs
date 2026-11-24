using System;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageMain : BaseMainMenuPage {
        public MainMenuPageMain(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        private WPopupData lastPopup;
        private Lifetime panelLife;

        protected override void BeforeUnload() {
            lastPopup?.Despawn();
        }

        protected override void LoadPage() {
            int idx = 0;
            if (!G.IsDemoMode) {
                ctx.AddButton(lifetime,idx++, T.MenuGameContinue, ContinueGame);
            }

            if (G.HasEnteredGame && !A.IsSaveTemplate) {
                if (A.S.IsPermadeath) {
                    // No Reload has no manual saves, the colony is saved when it is left
                    var saveBtn = ctx.AddButton(lifetime, idx++,
                        Texts.Strike(Texts.Grey(T.MenuSave)), UISounds.PlayActionDenied);
                    saveBtn.SetInteractable(false);
                    UIUtils.AddTip(lifetime, saveBtn, Texts.BoldQuotes(T.ModifierNoreloadDesc));
                } else {
                    ctx.AddTab(lifetime, idx++, T.MenuSave, ShowSaveMenu);
                }
            }

            // AddSubmenu(idx++, T.MenuTutorials, new MainMenuPageTutorials(this, ctx));
            AddSubmenu(idx++, T.MenuGameNew, new MainMenuPageNewGame(this, ctx));
            var hasSaves = SaveLoadUtils.AvailableSaves.Count > 0;
            // if (!hasSaves) {
            //     // make sure, perhaps save has appeared
            //     SaveLoadUtils.ClearCache();
            //     hasSaves = SaveLoadUtils.AvailableSaves.Count > 0;
            // }
            if (hasSaves) {
                ctx.AddTab(lifetime, idx++, T.MenuGameLoad, ShowLoadMenu);
            }

            ctx.AddSpacer(lifetime, idx++, 10);
            ctx.AddTab2(lifetime, idx++, T.MenuSettings, ShowSettings);
            // ctx.AddTab2(idx++, T.Achievements, ShowAchievements);
            // ctx.AddTab2(idx++, T.NounShips, ShowAchievements);
            #if STARDEUS_DEMO
            ctx.AddNotInDemo(lifetime, idx++, T.MenuMods);
            ctx.AddNotInDemo(lifetime, idx++, T.NounShips);
            #else
            AddSubmenu2(idx++, T.MenuMods, new MainMenuPageMods(this, ctx));
            AddSubmenu2(idx++, T.NounShips, new MainMenuPageShips(this, ctx));
            #endif
            AddSubmenu2(idx++, T.MenuAbout, new MainMenuPageAbout(this, ctx));
            idx = AddModSubmenus(idx);
            ctx.AddSpacer(lifetime, idx++, 48);
            ctx.AddButton3(lifetime, idx++, T.MenuExit, ShowExitChoice);

        }

        /// <summary>
        /// You can inject main menu pages with Harmony prefix / postfix patches here.
        /// Always increment and return idx.
        /// Example:
        /// AddSubmenu(idx++, "My Item", new MainMenuPage_MyItem(this, ctx));
        /// </summary>
        /// <param name="idx">menu item sibling index. Always increment by 1 when adding items.</param>
        /// <returns>incremented index</returns>
        private int AddModSubmenus(int idx) {
            return idx;
        }

        private void ShowSaveMenu(bool on) {
            if (panelLife != null) {
                panelLife.Dispose();
                panelLife = null;
            }
            if (on) {
                panelLife = lifetime.CreateChild("MainMenuSavePanel");
                new MainMenuSavePanel(ctx, panelLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }

        private void ShowSettings(bool on) {
            ctx.SettingsWidget.Toggle(on);
        }

        private void ShowLoadMenu(bool on) {
            if (panelLife != null) {
                panelLife.Dispose();
                panelLife = null;
            }
            if (on) {
                panelLife = lifetime.CreateChild("MainMenuLoadPanel");
                new MainMenuLoadPanel(ctx, panelLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }
        private void ContinueGame() {
            if (!G.HasEnteredGame) {
                Sounds.StopMusic();
                G.HasEnteredGame = true;
            }
            ctx.CloseMainMenu();
        }
        private void ShowExitChoice() {
            if (!G.HasEnteredGame || !GameState.IsCurrent(A.S) || A.IsSaveTemplate) {
                SaveLoadUtils.ExitToOS();
                return;
            }

            if (A.S.IsPermadeath) {
                SaveAndExit();
                return;
            }

            var qc = QuickChoice.Create(T.MenuExitOs);
            qc.AddButton(T.ExitWithNote, ShowNoteInput);
            qc.AddButton(T.MenuSaveAndExit, SaveAndExit);
            qc.AddButton(T.DontSave, ConfirmExitWithoutSave);
            qc.Show();
        }
        private void ConfirmExitWithoutSave() {
            lastPopup?.Despawn();
            lastPopup = WPopup.Create(true, IconId.CWarning, T.DontSave, T.AreYouSure,
                    T.UnsavedProgressWarning)
                .WithAction(T.Yes, SaveLoadUtils.ExitToOS)
                .WithAction2(T.Cancel, null)
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }
        private void ShowNoteInput() {
            lastPopup?.Despawn();
            lastPopup = WPopup.Create(true, IconId.WSaveGame, T.NoteToSelfTitle, T.NoteToSelfDesc)
                .WithInput(T.NoteToSelfLabel, T.NoteToSelfPlaceholder, null, text => {
                    A.S.UIQuery.SetNoteToSelf.Ask(text);
                    SaveAndExit();
                    return null;
                })
                .WithAction(T.MenuSaveAndExit, null)
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }
        private void SaveAndExit() {
            Coroutines.StartDirect(SaveLoadUtils.SaveAndExit());
        }
    }
}
