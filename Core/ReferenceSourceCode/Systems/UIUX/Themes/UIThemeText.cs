using Game.CodeGen;
using Game.Constants;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class UIThemeText {
        public Color Color { get; set; }
        public int FontSize { get; set; }
        public int FontSizeMin { get; set; }
        public int LineSpacing { get; set; }
        public int CharSpacing { get; set; }
        public bool AutoSize { get; set; }
        public bool IgnoreFontScaleUp;
        public bool KeepFontStyle;
        public TextOverflowModes? OverflowMode { get; set; }
        public int[] TextMarginLTRB { get; set; }
        public FontStyles FontStyles { get; set; }
        private float fontScale {
            get {
                var fs = The.Prefs.GetFloatNonZero(Pref.GUIFontScale, PrefH.GUIFontScale,
                    Consts.UIFontScaleDefault);
                if (IgnoreFontScaleUp && fs >= 1f) {
                    return 1f;
                }
                return fs;
            }
        }

        public void ApplyTo(TMP_Text text) {
            text.font = TMP_Settings.defaultFontAsset;
            text.color = Color;
            float? fs = null;
            if (FontSize > 0) {
                fs = fontScale;
                text.fontSize = FontSize * fs.Value;
                if (text.enableAutoSizing) {
                    text.fontSizeMax = FontSize * fs.Value;
                }
            }
            if (OverflowMode.HasValue) {
                text.overflowMode = OverflowMode.Value;
            }
            if (TextMarginLTRB != null && TextMarginLTRB.Length == 4) {
                text.margin = new Vector4(
                    TextMarginLTRB[0],
                    TextMarginLTRB[1],
                    TextMarginLTRB[2],
                    TextMarginLTRB[3]
                );
            }
            if (!KeepFontStyle) {
                text.fontStyle = FontStyles.Normal;
                if (FontStyles != default) {
                    text.fontStyle |= FontStyles;
                }
            }

            text.lineSpacing = LineSpacing;
            text.characterSpacing = CharSpacing;

            if (AutoSize) {
                fs ??= fontScale;
                if (FontSizeMin > 0) {
                    text.fontSizeMin = FontSizeMin * fs.Value;
                }
                if (FontSize > 0) {
                    text.fontSizeMax = FontSize * fs.Value;
                }
                text.enableAutoSizing = true;
            }
        }
    }
}
