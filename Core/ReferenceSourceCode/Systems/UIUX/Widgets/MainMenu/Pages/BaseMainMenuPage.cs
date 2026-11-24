using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public abstract class BaseMainMenuPage {
        protected readonly MainMenuContext ctx;
        protected readonly BaseMainMenuPage parent;
        protected List<BaseMainMenuPage> submenus = new();
        protected Lifetime lifetime;
        protected BaseMainMenuPage(BaseMainMenuPage parent, MainMenuContext ctx) {
            this.parent = parent;
            this.ctx = ctx;
        }

        public void AddSubmenu(int idx, string name, BaseMainMenuPage page) {
            submenus.Add(page);
            ctx.AddButton(lifetime, idx, name, page.Show);
        }
        public void AddSubmenu2(int idx, string name, BaseMainMenuPage page) {
            submenus.Add(page);
            ctx.AddButton2(lifetime, idx, name, page.Show);
        }
        protected void AddBack(int idx) {
            ctx.AddSpacer(lifetime, idx++, 48);
            ctx.AddButton3(lifetime, idx++, T.MenuBack, parent.Show);
        }

        public void Show() {
            ctx.CurrentPage?.Unload();
            ctx.CurrentPage = this;
            lifetime = ctx.Lifetime.CreateChild("MainMenuPage: " + GetType().Name);
            LoadPage();
            Coroutines.OnNextFrame(UIHoverFix.RefreshHover);
        }

        protected abstract void LoadPage();

        public bool DrillUp() {
            if (ctx.CloseTab()) {
                return true;
            }
            if (parent != null) {
                parent.Show();
                return true;
            }
            return false;
        }

        private void Unload() {
            BeforeUnload();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        protected virtual void BeforeUnload() { }
    }
}
