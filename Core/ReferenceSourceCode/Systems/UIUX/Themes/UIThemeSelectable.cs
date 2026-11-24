using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeSelectable {
        public Color Normal { get; set; }
        public Color Highlighted { get; set; }
        public Color Pressed { get; set; }
        public Color Selected { get; set; }
        public Color Disabled { get; set; }

        public void ApplyTo(Selectable selectable, float fadeTime) {
            var colors = new ColorBlock {
                normalColor = Normal,
                highlightedColor = Highlighted,
                pressedColor = Pressed,
                selectedColor = Selected,
                disabledColor = Disabled,
                colorMultiplier = 1f,
                fadeDuration = fadeTime
            };
            selectable.colors = colors;
        }
    }
}
