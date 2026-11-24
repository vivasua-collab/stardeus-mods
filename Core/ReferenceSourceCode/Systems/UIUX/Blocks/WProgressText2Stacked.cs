using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WProgressText2Stacked : WBase {
        public WProgressText2 Progress;
        [SerializeField] private Slider stackedSlider;
        [SerializeField] private Image stackedFill;
        public void SetStackedProgress(float progress) {
            stackedSlider.value = progress;
        }
        public void SetStackedColor(Color color) {
            stackedFill.color = color;
        }
    }
}
