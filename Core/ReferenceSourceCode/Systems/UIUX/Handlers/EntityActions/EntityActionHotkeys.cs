using System;
using System.Collections.Generic;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // Entity action hotkeys press the action's visible bottom bar button instead
    // of running the action themselves. What an action does lives in its button
    // closure, so this keeps a hotkey identical to a click (confirmations, error
    // messages of unavailable actions), makes "only with a compatible selection"
    // free - no button, no claim on the key - and needs nothing from the actions
    // beyond a label.
    public static class EntityActionHotkeys {
        public const string UIGroup = "entity";
        private static readonly Dictionary<string, WBottomBarButton> visible = new();
        // Keyed by HotkeyId, so that the bottom bar can look a binding up without
        // building the binding id string on every selection change
        private static readonly Dictionary<string, MultiBinding> byHotkeyId = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            visible.Clear();
            byHotkeyId.Clear();
        }

        public static string BindingId(string hotkeyId) {
            return $"ea.{hotkeyId}";
        }

        // Runs after mods are loaded, so their actions get bindings too, and
        // before the saved overrides are applied
        public static void Bind(Bindings bindings) {
            byHotkeyId.Clear();
            var actions = new List<BaseEntityAction>();
            foreach (var action in Registries.EntityActions.All.Values) {
                if (action.HotkeyLabel != null) {
                    actions.Add(action);
                }
            }
            // The registry is a dictionary, settings rows need a stable order
            actions.Sort((a, b) => string.Compare(a.HotkeyLabel, b.HotkeyLabel,
                StringComparison.CurrentCulture));
            foreach (var action in actions) {
                var hotkeyId = action.HotkeyId;
                var bindingId = BindingId(hotkeyId);
                var scope = (int) EntityActionScopes.Of(action);
                if (bindings.ById.ContainsKey(bindingId)) {
                    // Shared HotkeyId: label and default come from whichever
                    // action sorted first, so the sharing actions declare the same.
                    // The binding works wherever any of them does.
                    if (byHotkeyId.TryGetValue(hotkeyId, out var shared)) {
                        shared.ContextMask |= scope;
                    }
                    continue;
                }
                var owner = action;
                var binding = MultiBinding.CreateNamed(bindingId, () => owner.HotkeyLabel)
                    .InLayer(InputLayer.Selection)
                    .WithContextMask(scope)
                    .WithUIGroup(UIGroup)
                    .WithAvailability(() => IsVisible(hotkeyId))
                    .WithHandler(() => Press(hotkeyId));
                if (action.HotkeyDefault != null) {
                    binding.Bind(action.HotkeyDefault);
                }
                if (action.HotkeyDefaultGamepad != null) {
                    binding.Bind(action.HotkeyDefaultGamepad);
                }
                bindings.Add(binding);
                byHotkeyId.Add(hotkeyId, binding);
            }
        }

        public static void ClearVisible() {
            visible.Clear();
        }

        public static void Register(BaseEntityAction action, WBase widget) {
            if (action == null || widget is not WBottomBarButton button) {
                return;
            }
            var hotkeyId = action.HotkeyId;
            if (!byHotkeyId.TryGetValue(hotkeyId, out var binding)) {
                return;
            }
            // Every button shows the key, but only the first in bar order gets pressed
            button.SetShortcutGlyph(binding.Bindings.GetGlyphFor(binding));
            visible.TryAdd(hotkeyId, button);
        }

        public static bool IsVisible(string hotkeyId) {
            return visible.TryGetValue(hotkeyId, out var button)
                && button != null && button.gameObject.activeInHierarchy;
        }

        private static void Press(string hotkeyId) {
            if (visible.TryGetValue(hotkeyId, out var button) && button != null) {
                // The button's click sound comes from its pointer events, which a
                // key press never sends. Same sound as the F1-F10 overlay keys.
                UISounds.PlayClick(UISounds.ClickType.High, true);
                button.Item.Click();
            }
        }
    }
}
