using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeSliderDef {
        public string Selectable { get; set; }
        public string ImageBackground { get; set; }
        public string ImageRim { get; set; }
        public string ImageFill { get; set; }
        public string ImageHandle { get; set; }

        public void SetSlider(UITheme theme, Dictionary<string, UIThemeSlider> sliders, string id) {
            var slider = new UIThemeSlider();

            if (!string.IsNullOrWhiteSpace(Selectable)) {
                slider.Selectable = theme.GetSelectable(Selectable, this);
            }

            slider.ImageBackground = theme.GetImage(ImageBackground, this);
            slider.ImageRim = theme.GetImage(ImageRim, this);
            slider.ImageFill = theme.GetImage(ImageFill, this);
            slider.ImageHandle = theme.GetImage(ImageHandle, this);

            if (!sliders.TryAdd(id, slider)) {
                D.Err("UI Theme slider already exists by id: {0}", id);
            }
        }
    }
}
