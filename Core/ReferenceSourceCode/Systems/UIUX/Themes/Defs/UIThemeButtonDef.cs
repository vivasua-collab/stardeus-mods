using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeButtonDef {
        public string Image { get; set; }
        public string ImageActive { get; set; }
        public string Selectable { get; set; }
        public string Text { get; set; }

        public void SetButton(UITheme theme, Dictionary<string, UIThemeButton> buttons, string id) {
            var button = new UIThemeButton();

            if (!string.IsNullOrWhiteSpace(Image)) {
                button.Image = theme.GetImage(Image, id);
            }

            if (!string.IsNullOrWhiteSpace(ImageActive)) {
                button.ImageActive = theme.GetImage(ImageActive, id);
            }

            if (!string.IsNullOrWhiteSpace(Selectable)) {
                button.Selectable = theme.GetSelectable(Selectable, id);
            }

            if (!string.IsNullOrWhiteSpace(Text)) {
                button.Text = theme.GetText(Text, id);
            }

            if (!buttons.TryAdd(id, button)) {
                D.Err("UI Theme button already exists by id: {0}", id);
            }
        }
    }
}
