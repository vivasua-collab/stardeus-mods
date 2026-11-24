using Game.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WIconText2Progress : WBase {
        public WIconText2 Item;
        public Slider Slider;
        public void Load(string iconId, string text, float percent, string text2 = null) {
            Item.Load(iconId, text, text2);
            SetPercent(percent, text2 == null);
        }

        public void SetProgress(float progress, string text) {
            Slider.value = progress;
            Item.SetText2(text);
        }

        public void SetPercent(float progress, bool autoText = true) {
            Slider.value = progress;
            if (autoText) {
                Item.SetText2(Units.Percentage(progress));
            }
        }
    }
}
