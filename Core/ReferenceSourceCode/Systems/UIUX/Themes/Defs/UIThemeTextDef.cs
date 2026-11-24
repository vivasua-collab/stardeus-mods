using System;
using System.Collections.Generic;
using KL.Utils;
using TMPro;

namespace Game.Systems.UIUX {
    public sealed class UIThemeTextDef {
        public string Color { get; set; }
        public float Alpha { get; set; }
        public int FontSize { get; set; }
        public int FontSizeMin { get; set; }
        public bool AutoSize { get; set; }
        public bool IgnoreFontScaleUp { get; set; }
        public bool KeepFontStyle { get; set; }
        public int LineSpacing { get; set; }
        public int CharSpacing { get; set; }
        public string OverflowMode { get; set; }
        public int[] TextMarginLTRB { get; set; }
        public string[] FontStyle { get; set; }

        public void SetText(UITheme theme, Dictionary<string, UIThemeText> texts, string id) {
            var text = new UIThemeText();

            text.Color = theme.GetColor(Color, Alpha, this);
            text.FontSize = FontSize;
            text.FontSizeMin = FontSizeMin;
            text.AutoSize = AutoSize;
            text.KeepFontStyle = KeepFontStyle;
            text.LineSpacing = LineSpacing;
            text.CharSpacing = CharSpacing;
            text.IgnoreFontScaleUp = IgnoreFontScaleUp;

            if (!string.IsNullOrWhiteSpace(OverflowMode)) {
                if (Enum.TryParse<TextOverflowModes>(OverflowMode, out var mode)) {
                    text.OverflowMode = mode;
                } else {
                    D.Err("Invalid OverflowMode in theme {0} Text {1}: {2}. Valid modes: Overflow, Ellipsis, Masking, Truncate, ScrollRect, Page, Linked",
                        theme.Id, id, OverflowMode);
                }
            }

            text.TextMarginLTRB = TextMarginLTRB;

            if (FontStyle != null && FontStyle.Length > 0) {
                FontStyles fs = default;
                foreach (var f in FontStyle) {
                    if (Enum.TryParse<FontStyles>(f, out var ff)) {
                        fs |= ff;
                    } else {
                        D.Err("Invalid FontStyles in theme {0} Text {1}: {2}. Valid styles: Normal, Bold, Italic, Underline, LowerCase, UpperCase, SmallCaps, Strikethrough, Superscript, Subscript, Highlight",
                            theme.Id, id, f);
                    }
                }
                text.FontStyles = fs;
            }

            if (!texts.TryAdd(id, text)) {
                D.Err("UI Theme text already exists by id: {0}", id);
            }
        }
    }
}
