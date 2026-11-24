using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class MainMenuPageShips : BaseMainMenuPage {
        private Lifetime subLife;
        public MainMenuPageShips(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.NounShips);
            var shipsTab = ctx.AddTab2(lifetime, idx++, T.DownloadedShips, ShowShips);
            ctx.AddButton2(lifetime, idx++, Texts.ExternalLink(T.DownloadShips), OpenWorkshop_Ships);
            AddBack(idx);

            ctx.ToggleTab(shipsTab);
        }

        private void OpenWorkshop_Ships() {
            Files.OpenOSUrl("https://steamcommunity.com/workshop/browse/?appid=1380910&requiredtags[]=Ship");
        }

        private void ShowShips(bool on) {
            if (subLife != null) {
                subLife.Dispose();
                subLife = null;
            }
            if (on) {
                subLife = lifetime.CreateChild("ShipsSubPanel");
                new MainMenuShipsPanel(ctx, subLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }
    }
}
