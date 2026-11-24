using Game.Data;
using Game.Utils;
using KL.UI;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WBuildMenuButtonExpandable : WBase {
        public WButtonIconText Button;
        public WButtonIcon Expand;
        private Lifetime tipLifetime;
        private CodexTooltip codexTip;
        private bool overButton;
        private bool overExpand;

        // The arrow is a child of the button, so hovering it also hovers the button. The codex
        // tooltip shows only while the pointer is on the button but not on the arrow.
        public void LoadCodexTooltip(Lifetime life, Def def) {
            tipLifetime = life;
            codexTip = CodexTooltip.CreateFor(Button, def, optional: false, showDelay: 0.1f,
                anchorTop: false);
            HoverOf(Button.gameObject).SetCallback(on => {
                overButton = on;
                RefreshCodexTooltip();
            }, null, clearOnDeactivate: true);
            HoverOf(Expand.gameObject).SetCallback(on => {
                overExpand = on;
                RefreshCodexTooltip();
            }, null, clearOnDeactivate: true);
        }

        private static UIHoverCallback HoverOf(GameObject go) {
            if (!go.TryGetComponent<UIHoverCallback>(out var hover)) {
                hover = go.AddComponent<UIHoverCallback>();
            }
            return hover;
        }

        private void RefreshCodexTooltip() {
            if (overButton && !overExpand) {
                A.S.UIQuery.ShowCodexTooltipFor.Ask(tipLifetime, codexTip);
            } else {
                A.S.UIQuery.HideCodexTooltip.Ask();
            }
        }

        private void OnDisable() {
            overButton = false;
            overExpand = false;
        }
    }
}
