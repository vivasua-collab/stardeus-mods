using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeSlider {
        public UIThemeSelectable Selectable { get; set; }
        public UIThemeImage ImageBackground { get; set; }
        public UIThemeImage ImageRim { get; set; }
        public UIThemeImage ImageFill { get; set; }
        public UIThemeImage ImageHandle { get; set; }

        public void ApplyTo(Transform target, float fadeTime) {
            if (target == null) { return; }

            if (Selectable != null && target.TryGetComponent<Slider>(out var slider)) {
                Selectable?.ApplyTo(slider, fadeTime);
            }
            foreach (var image in target.GetComponentsInChildren<Image>(
                    includeInactive: true)) {
                if (image.name == "Background") {
                    ImageBackground?.ApplyTo(image);
                } else if (image.name == "Rim") {
                    ImageRim?.ApplyTo(image);
                } else if (image.name == "Fill") {
                    ImageFill?.ApplyTo(image);
                } else if (image.name == "Handle") {
                    ImageHandle?.ApplyTo(image);
                }
            }
        }
    }
}
