using System;
using Game.Commands;
using Game.Constants;
using Game.Scenarios;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageNewGame : BaseMainMenuPage {
        public MainMenuPageNewGame(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.GameNew);
            if (A.IsSaveTemplate) {
                ctx.AddButton2(lifetime, idx++, T.GameTemplateNew, () => {
                    A.S.UIQuery.CloseMainMenu.Ask(true);
                    new CloneGameUI(The.R.State).Show();
                });
            }

            AddSubmenu2(idx++, T.GameStoryMode, new MainMenuPageNewGame_StoryMode(this, ctx));
            AddSubmenu2(idx++, T.GameCustomScenario, new MainMenuPageNewGame_Custom(this, ctx));
            #if STARDEUS_DEMO
            ctx.AddNotInDemo(lifetime, idx++, T.ScenarioSandbox);
            #else
            var sandbox = Scenario.Find(SandboxScenario.ScenarioId);
            var btn = ctx.AddButton2(lifetime, idx++, T.ScenarioSandbox, () => ShowSandbox(sandbox));
            UIUtils.AddTip(lifetime, btn, sandbox.DescT);
            #endif
            AddBack(idx);
        }

        private void ShowSandbox(Scenario scenario) {
            CmdNewGame.QuickStartFor(scenario);
        }
    }
}
