using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageTutorials : BaseMainMenuPage {
        public MainMenuPageTutorials(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.GameNew);
            ctx.AddButton2(lifetime, idx++, "TODO", null);
            AddBack(idx);
        }
    }
}
