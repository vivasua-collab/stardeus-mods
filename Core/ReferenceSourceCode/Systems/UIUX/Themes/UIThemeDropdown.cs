using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeDropdown {
        public UIThemeImage ImageBackground { get; set; }
        public UIThemeImage ImageBackgroundItem { get; set; }
        public UIThemeImage ImageArrow { get; set; }
        public UIThemeImage ImageArrowItem { get; set; }
        public UIThemeSelectable SelectableDropdown { get; set; }
        public UIThemeSelectable SelectableItem { get; set; }
        public UIThemeText Text { get; set; }
        public UIThemeText TextItem { get; set; }

        public void ApplyTo(Transform target, float fadeTime) {
            if (target == null) { return; }

            var dropdown = target.GetComponent<TMP_Dropdown>();
            if (dropdown == null) {
                D.Err("UIThemeDropdown.ApplyTo called on transform without TMP_Dropdown component: {0}",
                    D.GOPath(target));
                return;
            }

            // Apply main text
            if (dropdown.captionText != null) {
                // D.Warn("Applying style to caption: {0}", D.GOPath(dropdown.transform));
                Text?.ApplyTo(dropdown.captionText);
            }

            // Apply item text
            if (dropdown.itemText != null) {
                TextItem?.ApplyTo(dropdown.itemText);
            }

            // Apply selectable to dropdown
            SelectableDropdown?.ApplyTo(dropdown, fadeTime);

            // Find and apply images
            var images = target.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (var image in images) {
                if (image.name == "Background") {
                    ImageBackground?.ApplyTo(image);
                } else if (image.name == "Arrow" && image.transform.parent == target) {
                    ImageArrow?.ApplyTo(image);
                } else if (image.name == "Item Checkmark") {
                    ImageArrowItem?.ApplyTo(image);
                } else if (image.name == "Item Background") {
                    ImageBackgroundItem?.ApplyTo(image);
                }
            }

            // Apply item selectable
            if (dropdown.itemText != null) {
                var itemParent = dropdown.itemText.transform.parent;
                if (itemParent != null) {
                    var itemToggle = itemParent.GetComponent<Toggle>();
                    if (itemToggle != null) {
                        SelectableItem?.ApplyTo(itemToggle, fadeTime);
                    }
                }
            }
        }
    }
}
