using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeInputDef {
        public string HighlightColor { get; set; }
        public float HighlightAlpha { get; set; }
        public string Selectable { get; set; }
        public string TextInput { get; set; }
        public string TextPlaceholder { get; set; }
        public string Image { get; set; }

        public void SetInput(UITheme theme,
                Dictionary<string, UIThemeInput> inputs, string id) {
            var input = new UIThemeInput();

            if (!string.IsNullOrWhiteSpace(HighlightColor)) {
                input.HighlightColor = theme.GetColor(HighlightColor,
                    HighlightAlpha > 0 ? HighlightAlpha : 1f, this);
            }

            if (!string.IsNullOrWhiteSpace(Selectable)) {
                input.Selectable = theme.GetSelectable(Selectable, this);
            }

            if (!string.IsNullOrWhiteSpace(TextInput)) {
                input.TextInput = theme.GetText(TextInput, this);
            }

            if (!string.IsNullOrWhiteSpace(TextPlaceholder)) {
                input.TextPlaceholder = theme.GetText(TextPlaceholder, this);
            }

            if (!string.IsNullOrWhiteSpace(Image)) {
                input.Image = theme.GetImage(Image, this);
            }

            if (!inputs.TryAdd(id, input)) {
                D.Err("UI Theme input already exists by id: {0}", id);
            }
        }
    }
}
