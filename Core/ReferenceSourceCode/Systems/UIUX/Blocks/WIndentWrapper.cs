using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WIndentWrapper : WGroup {
        [SerializeField] private VerticalLayoutGroup layout;
        public void SetIndent(int left) {
            var p = layout.padding;
            p.left = left;
            layout.padding = p;
        }
    }
}
