using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeScrollbar {
        public UIThemeImage ImageHandle { get; set; }
        public UIThemeImage ImageScroll { get; set; }
        public UIThemeSelectable Selectable { get; set; }

        public void ApplyTo(Transform target, float fadeTime) {
            if (target == null) { return; }

            // bool foundMainImage = false;
            foreach (var image in target.GetComponentsInChildren<Image>(includeInactive: true)) {
                if (image.name == "Handle") {
                    ImageHandle?.ApplyTo(image);
                } else if (image.name.Contains("Scrollbar")) {
                    // foundMainImage = true;
                    ImageScroll?.ApplyTo(image);
                    var scrollbar = image.GetComponent<Scrollbar>();
                    if (scrollbar != null) {
                        Selectable?.ApplyTo(scrollbar, fadeTime);
                    }
                }
            }

            // some scrollbars may not have a background image, it's ok
            // if (!foundMainImage) {
            //     D.Err("A scrollbar game object MUST have a Scrollbar in the name. This one does not: {0}",
            //         D.GOPath(target));
            // }
        }
    }
}
