using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeDropdownDef {
        public string ImageBackground { get; set; }
        public string ImageBackgroundItem { get; set; }
        public string ImageArrow { get; set; }
        public string ImageArrowItem { get; set; }
        public string SelectableDropdown { get; set; }
        public string SelectableItem { get; set; }
        public string Text { get; set; }
        public string TextItem { get; set; }

        public void SetDropdown(UITheme theme, Dictionary<string, UIThemeDropdown> dropdowns, string id) {
            var dropdown = new UIThemeDropdown();

            dropdown.ImageBackground = theme.GetImage(ImageBackground, this);
            dropdown.ImageBackgroundItem = theme.GetImage(ImageBackgroundItem, this);
            dropdown.ImageArrow = theme.GetImage(ImageArrow, this);
            dropdown.ImageArrowItem = theme.GetImage(ImageArrowItem, this);

            if (!string.IsNullOrWhiteSpace(SelectableDropdown)) {
                dropdown.SelectableDropdown = theme.GetSelectable(SelectableDropdown, this);
            }

            if (!string.IsNullOrWhiteSpace(SelectableItem)) {
                dropdown.SelectableItem = theme.GetSelectable(SelectableItem, this);
            }

            if (!string.IsNullOrWhiteSpace(Text)) {
                dropdown.Text = theme.GetText(Text, this);
            }

            if (!string.IsNullOrWhiteSpace(TextItem)) {
                dropdown.TextItem = theme.GetText(TextItem, this);
            }

            if (!dropdowns.TryAdd(id, dropdown)) {
                D.Err("UI Theme dropdown already exists by id: {0}", id);
            }
        }
    }
}
