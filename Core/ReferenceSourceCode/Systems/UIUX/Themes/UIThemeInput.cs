using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeInput {
        public Color HighlightColor { get; set; }
        public UIThemeSelectable Selectable { get; set; }
        public UIThemeText TextInput { get; set; }
        public UIThemeText TextPlaceholder { get; set; }
        public UIThemeImage Image { get; set; }

        public void ApplyTo(Transform target, float selectableFadeTime) {
            if (target == null) { return; }

            if (!target.TryGetComponent<TMP_InputField>(out var input)) {
                return;
            }

            // Apply selection/highlight color
            input.selectionColor = HighlightColor;

            // Apply background image
            if (Image != null && target.TryGetComponent<Image>(out var img)) {
                Image.ApplyTo(img);
            }

            // Apply selectable
            if (Selectable != null) {
                Selectable.ApplyTo(input, selectableFadeTime);
            }

            // Apply text style to the text component
            if (TextInput != null && input.textComponent != null) {
                TextInput.ApplyTo(input.textComponent);
            }

            if (TextPlaceholder != null && input.placeholder != null) {
                if (input.placeholder.TryGetComponent<TMP_Text>(out var placeholderText)) {
                    TextPlaceholder.ApplyTo(placeholderText);
                }
            }
        }
    }
}
