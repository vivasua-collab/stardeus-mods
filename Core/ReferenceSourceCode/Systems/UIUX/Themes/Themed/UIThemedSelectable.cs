using System;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIThemedSelectable : UIThemedBase {
        public Action OnApply;

        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetSelectable(EffectiveConfigId, gameObject);
            var selectable = GetComponent<Selectable>();
            if (selectable == null) {
                D.Err("UIThemedSelectable has no Selectable comp on the game object! {0}->{1}",
                    EffectiveConfigId, D.GOPath(transform));
                return;
            }
            cfg?.ApplyTo(selectable, The.UITheme.SelectableFadeTime);
            OnApply?.Invoke();
        }
    }
}
