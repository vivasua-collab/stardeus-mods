using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeScrollbarDef {
        public string ImageHandle { get; set; }
        public string ImageScroll { get; set; }
        public string Selectable { get; set; }

        public void SetScrollbar(UITheme theme, Dictionary<string, UIThemeScrollbar> scrollbars, string id) {
            var scrollbar = new UIThemeScrollbar();

            if (!string.IsNullOrWhiteSpace(ImageHandle)) {
                scrollbar.ImageHandle = theme.GetImage(ImageHandle, this);
            }

            if (!string.IsNullOrWhiteSpace(ImageScroll)) {
                scrollbar.ImageScroll = theme.GetImage(ImageScroll, this);
            }

            if (!string.IsNullOrWhiteSpace(Selectable)) {
                scrollbar.Selectable = theme.GetSelectable(Selectable, this);
            }

            if (!scrollbars.TryAdd(id, scrollbar)) {
                D.Err("UI Theme scrollbar already exists by id: {0}", id);
            }
        }
    }
}
