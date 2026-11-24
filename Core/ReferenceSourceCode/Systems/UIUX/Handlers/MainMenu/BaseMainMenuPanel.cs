using System;
using System.CodeDom;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Platform;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public abstract class BaseMainMenuPanel {
        protected abstract string Id { get; }
        protected readonly MainMenuContext ctx;
        private readonly Lifetime parentLife;
        protected Lifetime lifetime;
        public BaseMainMenuPanel(MainMenuContext ctx, Lifetime parentLife) {
            this.ctx = ctx;
            this.parentLife = parentLife;
            LoadContents();
        }

        protected void LoadContents() {
            // Dispose the previous content lifetime (this panel's prior load, or a
            // prior panel's). Dispose() is idempotent, so an already-disposed one is a no-op.
            if (ctx.PanelLifetime != null) {
                ctx.PanelLifetime.Dispose();
            }
            lifetime = parentLife.CreateChild(Id);
            ctx.PanelLifetime = lifetime;
            OnLoadContents();
        }

        protected abstract void OnLoadContents();
    }
}
