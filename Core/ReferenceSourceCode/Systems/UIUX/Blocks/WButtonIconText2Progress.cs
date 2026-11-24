using System;
using Game.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WButtonIconText2Progress : WBase {
        public WButtonIconText2 Item;
        public Slider Slider;
        public void Load(string iconId, string text, float percent, Action onClick, string text2 = null) {
            Item.Load(iconId, text, text2, onClick);
            SetPercent(percent, text2 == null);
        }

        public void SetPercent(float progress, bool autoText = true) {
            Slider.value = progress;
            if (autoText) {
                Item.SetText2(Units.Percentage(progress, clamp: false));
            }
        }
    }
}
