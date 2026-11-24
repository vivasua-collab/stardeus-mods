using Game.Constants;
using Game.Input;
using Game.Utils;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX.Codex {
    public static class CodexUtils {
        public static bool IsGamepad => VirtualMouseInput.IsCurrent;
        public static string CancelGlyph => GetControlName(ActionType.ClickCancel);
        public static string EscGlyph => GetControlName(ActionType.Pause);
        public static string RotateGlyph => GetControlName(ActionType.Rotate);
        public static string ZoomGlyph => GetControlName(ActionType.CameraZoom);
        public static string ClickGlyph => GetControlName(ActionType.Click);
        public static string CtrlGlyph => GetControlName(ActionType.CtrlModifier);
        public static string AltGlyph => GetControlName(ActionType.AltModifier);
        public static string ShiftGlyph => GetControlName(ActionType.ShiftModifier);
        public static string TabGlyph => GetControlName(ActionType.QuickSearch);
        public static string ResearchGlyph => GetControlName(ActionType.OpenResearch);
        public static string StarmapGlyph => GetControlName(ActionType.OpenStarmap);
        public static string BuildMenuGlyph => GetControlName(ActionType.ShortcutPlaceTiles);

        public static string GetControlName(ActionType act, bool anyInputMethod = false) {
            if (CodexExportScope.IsActive) {
                return CodexExportScope.ResolveGlyph(act) ?? $"[{T.ShortcutNotBound}]";
            }
            if (act == ActionType.Pause) {
                if (!VirtualMouseInput.IsCurrent) {
                    var gl = GlyphMap.Sprite("Keyboard", "escape");
                    var keyName = Keyboard.current.escapeKey.displayName;
                    if (string.IsNullOrWhiteSpace(keyName)) {
                        keyName = "Esc";
                    }
                    var tip = $"{T.InputKeyboard}: {keyName}";
                    tip = Texts.ToBase64Url(tip);
                    return $"<link=\"tip:{tip}\">{gl}</link>";
                }
            }
            var glyph = The.Bindings.GetBinding(act)
                .AllControlGlyphs(out var tt, " / ");
            if (string.IsNullOrEmpty(tt)) {
                if (act == ActionType.AltModifier && The.Platform.IsSteamDeck()) {
                    return Texts.Bold("[R4]");
                }
                return $"[{T.ShortcutNotBound}]";
            }
            var g = $"<link=\"tip:{Texts.ToBase64Url(tt)}\">{glyph}</link>";
            return g;
            //return The.Bindings.GetControlName(act, anyInputMethod: anyInputMethod);
        }
    }
}
