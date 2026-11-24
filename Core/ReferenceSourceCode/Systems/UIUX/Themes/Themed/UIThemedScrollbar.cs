using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIThemedScrollbar : UIThemedBase {
        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetScrollbar(EffectiveConfigId, gameObject);
            if (cfg == null) {
                D.Err("UIThemedScrollbar config not found: {0}->{1}",
                    EffectiveConfigId, D.GOPath(transform));
                return;
            }
            cfg.ApplyTo(transform, The.UITheme.SelectableFadeTime);
        }
    }
}
