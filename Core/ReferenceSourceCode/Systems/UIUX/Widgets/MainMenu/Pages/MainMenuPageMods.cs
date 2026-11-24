using System;
using Game.Components;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class MainMenuPageMods : BaseMainMenuPage {
        private Lifetime subLife;
        public MainMenuPageMods(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.MenuMods);
            var modsTab = ctx.AddTab2(lifetime, idx++, "Installed Mods", ShowMods);
            ctx.AddButton2(lifetime, idx++, Texts.ExternalLink(T.DownloadMods), OpenWorkshop_Mods);
            ctx.AddTab2(lifetime, idx++, "Create Mods", ShowCreateMods);
            AddBack(idx);

            ctx.ToggleTab(modsTab);
        }

        private void ShowCreateMods(bool on) {
            if (subLife != null) {
                subLife.Dispose();
                subLife = null;
            }
            if (on) {
                subLife = lifetime.CreateChild("CreateModsSubPanel");
                new MainMenuCreateModsPanel(ctx, subLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }

        private void OpenWorkshop_Mods() {
            // https://steamcommunity.com/app/1380910/workshop/
            Files.OpenOSUrl("https://steamcommunity.com/workshop/browse/?appid=1380910&requiredtags[]=Mod");
        }

        private void ShowMods(bool on) {
            if (subLife != null) {
                subLife.Dispose();
                subLife = null;
            }
            if (on) {
                subLife = lifetime.CreateChild("ModsSubPanel");
                new MainMenuModsPanel(ctx, subLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }
    }
}
