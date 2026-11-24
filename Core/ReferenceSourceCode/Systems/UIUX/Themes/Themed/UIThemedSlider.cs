using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIThemedSlider : UIThemedBase {
        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetSlider(EffectiveConfigId, gameObject);
            if (cfg == null) {
                D.Err("UIThemedSlider config not found: {0}->{1}",
                    EffectiveConfigId, D.GOPath(transform));
                return;
            }
            cfg.ApplyTo(transform, The.UITheme.SelectableFadeTime);
        }
    }
}
