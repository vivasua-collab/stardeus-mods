using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIThemedImage : UIThemedBase {
        private Image image;
        protected override void ApplyTheme() {
            var cfg = The.UITheme.GetImage(EffectiveConfigId, gameObject);
            image ??= GetComponent<Image>();
            if (image == null) {
                return;
            }
            cfg?.ApplyTo(image);
        }

        public void OverrideColor(string colorId) {
            var col = The.UITheme.GetColor(colorId, "UIThemedImage override color");
            image ??= GetComponent<Image>();
            image.color = col;
        }

        public void ClearColorOverride() {
            ApplyTheme();
        }
    }
}
