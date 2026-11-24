using Game.Constants;
using Game.Scenarios;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageNewGame_Custom : BaseMainMenuPage {
        public MainMenuPageNewGame_Custom(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }
        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.GameCustomScenario);
            foreach (var sc in Scenario.PublicScenarios) {
                #if STARDEUS_DEMO
                if (sc != Scenario.DefaultScenario) {
                    ctx.AddNotInDemo(lifetime, idx++, sc.NameT);
                    continue;
                }
                #endif
                var btn = ctx.AddButton2(lifetime, idx++, sc.NameT, () => ConfigureScenario(sc));
                UIUtils.AddTip(lifetime, btn, sc.DescT);
            }
            AddBack(idx);
        }

        private void ConfigureScenario(Scenario scenario) {
            A.S.UIQuery.ShowGameSetup.Ask(scenario, true);
        }
    }
}
