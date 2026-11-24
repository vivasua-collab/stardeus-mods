using System;
using Game.Constants;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class MainMenuPageAbout : BaseMainMenuPage {
        private Lifetime subLife;
        private WTabButton changelogTab;
        public MainMenuPageAbout(BaseMainMenuPage parent, MainMenuContext ctx) : base(parent, ctx) {
        }

        protected override void LoadPage() {
            int idx = 0;
            ctx.AddSubHeader(lifetime, idx++, T.MenuAbout);
            ctx.AddTab2(lifetime, idx++, T.Credits, ShowCredits);
            changelogTab = ctx.AddTab2(lifetime, idx++, T.Changelog, ShowChangelog);
            ctx.AddTab2(lifetime, idx++, T.NounWebLinks, ShowAboutTab);
            AddBack(idx);
        }

        public void OpenChangelog() {
            ctx.ToggleTab(changelogTab);
        }

        private void ShowAboutTab(bool on) {
            DisposeSubLife();
            if (on) {
                subLife = lifetime.CreateChild("AboutSubPanel");
                new MainMenuAboutPanel(ctx, subLife);
            } else {
                ctx.SubMenuPanel.Cleanup();
                ctx.SubMenuPanel.Toggle(false);
            }
        }
        private void ShowChangelog(bool on) {
            DisposeSubLife();
            if (on) {
                subLife = lifetime.CreateChild("ChangelogSubPanel");
                new MainMenuChangelogPanel(ctx.WallOfTextWidget, ctx, subLife);
            } else {
                ctx.WallOfTextWidget.Toggle(false);
            }
        }

        private void DisposeSubLife() {
            if (subLife != null) {
                subLife.Dispose();
                subLife = null;
            }
        }

        private void ShowCredits(bool on) {
            DisposeSubLife();
            if (on) {
                var credits = Texts.Beautify(Res.LoadText("Credits"));
                ctx.WallOfTextWidget.Load(credits, CloseTab, centered: true);
                ctx.WallOfTextWidget.SetActive(true);
            } else {
                ctx.WallOfTextWidget.Toggle(false);
            }
        }

        private void CloseTab() {
            ctx.CloseTab();
        }
    }
}
