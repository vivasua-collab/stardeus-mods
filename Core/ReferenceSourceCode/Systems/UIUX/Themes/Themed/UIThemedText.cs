using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {

    [DisallowMultipleComponent]
    public class UIThemedText : UIThemedBase {
        [SerializeField] private bool recursive;

        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetText(EffectiveConfigId, gameObject);
            if (cfg == null) { return; }
            int numApplied = 0;
            if (recursive) {
                foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive: true)) {
                    numApplied++;
                    cfg.ApplyTo(text);
                }
            } else {
                var text = GetComponent<TMP_Text>();
                if (text != null) {
                    numApplied++;
                    cfg.ApplyTo(text);
                }
            }

            if (numApplied == 0) {
                D.Err("UIThemedText has no TMP_Text comp on the game object! Recursive: {0}. {1}->{2}",
                    recursive, EffectiveConfigId, D.GOPath(transform));
            }
        }
    }
}
