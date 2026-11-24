using System.Collections.Generic;

namespace Game.Systems.UIUX {
    public sealed class UIThemeDef {
        public string Id;
        public string Name;
        public Dictionary<string, string> Colors;
        public Dictionary<string, UIThemeImageDef> Images;
        public Dictionary<string, UIThemeButtonDef> Buttons;
        public Dictionary<string, UIThemeDropdownDef> Dropdowns;
        public Dictionary<string, UIThemeSliderDef> Sliders;
        public Dictionary<string, UIThemeScrollbarDef> Scrollbars;
        public Dictionary<string, UIThemeInputDef> Inputs;
        public float SelectableFadeTime;
        public Dictionary<string, UIThemeSelectableDef> Selectables;
        public Dictionary<string, UIThemeTextDef> Texts;
    }
}
