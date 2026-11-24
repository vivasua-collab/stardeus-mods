using Game.Constants;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WContentBlock : WGroup {
        public override string ToString() {
            return "WContentBlock[]";
        }

        private void OnEnable() {
            if (Rect.localScale.x != 1f) {
                D.Err("Content block has non-one scale! This can cause issues with layout. {0}", D.GOPath(transform));
            }
        }
        private void OnDisable() {
            if (Rect.localScale.x != 1f) {
                D.Err("Content block has non-one scale! This can cause issues with layout. {0}", D.GOPath(transform));
            }
        }
    }
}
