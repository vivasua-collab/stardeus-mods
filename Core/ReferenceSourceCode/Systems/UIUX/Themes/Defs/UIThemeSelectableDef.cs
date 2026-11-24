using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UIThemeSelectableDef {
        public sealed class SelectableColors {
            public string Normal { get; set; }
            public string Highlighted { get; set; }
            public string Pressed { get; set; }
            public string Selected { get; set; }
            public string Disabled { get; set; }
        }
        public SelectableColors Colors { get; set; }
        public float Alpha { get; set; }

        public void SetSelectable(UITheme theme, Dictionary<string, UIThemeSelectable> selectables, string id) {
            var selectable = new UIThemeSelectable();

            var a = Alpha;
            if (a == 0f) {
                a = 1f;
            }

            if (Colors != null) {
                selectable.Normal = theme.GetColor(Colors.Normal, a, this);
                selectable.Highlighted = theme.GetColor(Colors.Highlighted, a, this);
                selectable.Pressed = theme.GetColor(Colors.Pressed, a, this);
                selectable.Selected = theme.GetColor(Colors.Selected, a, this);
                selectable.Disabled = theme.GetColor(Colors.Disabled, a, this);
            } else {
                D.Err("Selectable {0} has no Colors defined", id);
            }

            if (!selectables.TryAdd(id, selectable)) {
                D.Err("UI Theme selectable already exists by id: {0}", id);
            }
        }
    }

}
