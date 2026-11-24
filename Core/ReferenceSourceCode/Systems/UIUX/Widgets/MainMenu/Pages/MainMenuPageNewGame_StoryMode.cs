using System;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Input;
using Game.Modifiers;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageNewGame_StoryMode : BaseMainMenuPage {
        public MainMenuPageNewGame_StoryMode(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.GameStoryMode);
            var quickStartBtn = ctx.AddButton2(lifetime, idx++, T.GameQuickStart, QuickStart);
            // The difficulty is picked by the question that follows the click
            UIUtils.AddTip(lifetime, quickStartBtn, Scenario.DefaultScenario.DescT);
            var customStartBtn = ctx.AddButton2(lifetime, idx++, T.GameCustomStart, CustomStart);
            UIUtils.AddTip(lifetime, customStartBtn, Scenario.DefaultScenario.DescT);
            AddBack(idx);
        }

        private void CustomStart() {
            var scenario = Scenario.DefaultScenario;
            A.S.UIQuery.ShowGameSetup.Ask(scenario, true);
        }

        // Quick Start skips the setup screen, so one question picks the difficulty: a
        // newcomer gets Relaxing, a colony sim veteran gets Balanced. Both get the same story
        // beats at the same times; the difficulty only changes how hard they hit
        private void QuickStart() {
            var newcomer = Difficulty.Find(Difficulty.QuickStartId);
            var veteran = Difficulty.Find(Difficulty.DefaultId);
            var qc = QuickChoice.Create(T.QuickstartExperiencedQuestion);
            qc.AddButton($"{Texts.Accented(T.No)} {GlyphMap.ArrowRight} {T.GameDifficulty}: {newcomer.Name}", () => ConfirmQuickStart(newcomer.Id));
            qc.AddButton($"{Texts.Accented(T.Yes)} {GlyphMap.ArrowRight} {T.GameDifficulty}: {veteran.Name}", () => ConfirmQuickStart(veteran.Id));
            qc.Show();
        }

        private void ConfirmQuickStart(string difficultyId) {
            if (G.HasEnteredGame) {
                WPopup.Create(true, IconId.CWarning, T.MenuGameName, T.UnsavedProgressWarning)
                    .WithAction(T.Confirm, () => DoQuickStart(difficultyId))
                    .WithAction2(T.Cancel, null)
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            } else {
                DoQuickStart(difficultyId);
            }
        }

        private void DoQuickStart(string difficultyId) {
            CmdNewGame.QuickStartFor(Scenario.DefaultScenario, difficultyId);
        }

    }
}
