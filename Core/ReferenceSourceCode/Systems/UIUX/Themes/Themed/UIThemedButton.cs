using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIThemedButton : UIThemedBase {
        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetButton(EffectiveConfigId, gameObject);
            if (cfg == null) {
                D.Err("UIThemedButton config not found: {0}->{1}",
                    EffectiveConfigId, D.GOPath(transform));
                return;
            }
            cfg.ApplyTo(transform, The.UITheme.SelectableFadeTime);
        }
    }
}
