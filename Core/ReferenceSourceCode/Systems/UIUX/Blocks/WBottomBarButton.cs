using System;
using Game.Input;
using Game.Utils;
using KL.Pool;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WBottomBarButton : WBase, ICustomPoolable {
        public WButtonIconText Item;
        public WText ShortcutGlyph;
        public WText Badge;
        private Action onPressed;
        public void Load(string icon, string text, Action onClick) {
            Item.Load(icon, text, WithPressed(onClick));
            SetShortcutGlyph(null);
        }
        public void LoadWithShortcut(ActionType shortcut, string icon, string text, Action onClick) {
            Item.Load(icon, text, WithPressed(onClick));
            SetShortcutGlyph(shortcut);
        }
        public void LoadMulti(int cnt, string icon, string text, Action onClick) {
            Item.Load(icon, text, WithPressed(onClick));
            SetCount(cnt);
            SetShortcutGlyph(null);
        }

        // Runs before the button's own click, whether clicked or pressed by its hotkey (both
        // end in Item.Click). Lets the bar tell that the player used one of the selection's
        // actions without touching any of the actions.
        public void SetOnPressed(Action act) {
            onPressed = act;
        }

        private Action WithPressed(Action onClick) {
            // Unwrapped, so an unrigged button still reports itself
            if (onClick == null) { return null; }
            return () => {
                // First: the click may rebuild the bar and pool this button
                onPressed?.Invoke();
                onClick();
            };
        }

        private void SetCount(int cnt) {
            // The disabled prefab has no count badge: SpawnButtonMulti hands it out when the
            // action does not apply, and a disabled button needs no count
            if (Badge == null) { return; }
            Badge.Load(Units.XNum(cnt));
            Badge.Text.ForceMeshUpdate();
            Badge.RebuildLayout();
        }

        public void LoadMultiWithShortcut(ActionType shortcut, int cnt, string icon, string text, Action onClick) {
            LoadWithShortcut(shortcut, icon, text, onClick);
            SetCount(cnt);
        }

        public void PoolActivate() {
            onPressed = null;
            ShortcutGlyph.SetActiveFast(false);
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
        }

        public void SetShortcutGlyph(ActionType act) {
            var glyph = The.Bindings.GetGlyphFor(act);
            SetShortcutGlyph(glyph);
        }
        public void SetShortcutGlyph(string glyph) {
            if (string.IsNullOrWhiteSpace(glyph)) {
                ShortcutGlyph.SetActiveFast(false);
                return;
            }
            ShortcutGlyph.SetText(glyph);
            ShortcutGlyph.SetActiveFast(true);
        }
    }
}
