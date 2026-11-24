using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WProgress : WBase {
        [SerializeField] private Slider slider;
        [SerializeField] private Image fill;
        public void Load(Color color, float value) {
            fill.color = color;
            slider.value = value;
        }
        public void SetValue(float value) {
            slider.value = value;
        }
        public void SetColor(Color color) {
            fill.color = color;
        }
    }
}
