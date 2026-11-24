using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeButton {
        public UIThemeImage Image { get; set; }
        public UIThemeImage ImageActive { get; set; }
        public UIThemeSelectable Selectable { get; set; }
        public UIThemeText Text { get; set; }

        public void ApplyTo(Transform target, float selectableFadeTime) {
            if (target == null) { return; }

            // Apply selectable
            if (Selectable != null && target.TryGetComponent<Selectable>(out var selectable)) {
                selectable.enabled = false;
                Selectable.ApplyTo(selectable, selectableFadeTime);
                // Apply background image
                if (Image != null && selectable.targetGraphic is Image img) {
                    Image.ApplyTo(img);
                }
                selectable.enabled = true;
            }

            // Apply text to all children
            if (Text != null) {
                var texts = target.GetComponentsInChildren<TMP_Text>(includeInactive: true);
                foreach (var text in texts) {
                    if (text.TryGetComponent<UIThemedText>(out var themed)) {
                        themed.ApplyStyle();
                        // it has it's own style
                        continue;
                    }
                    // there's an override - let's not touch the style
                    Text.ApplyTo(text);
                }
            }

            // Apply active overlay
            if (ImageActive != null) {
                var activeOverlay = target.Find("ActiveOverlay");
                if (activeOverlay != null && activeOverlay.TryGetComponent<Image>(out var activeImg)) {
                    ImageActive.ApplyTo(activeImg);
                }
            }
        }
    }
}
