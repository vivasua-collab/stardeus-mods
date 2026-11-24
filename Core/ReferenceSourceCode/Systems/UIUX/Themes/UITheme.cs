using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.CodeGen;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Steamworks;
using UnityEngine;
namespace Game.Systems.UIUX {
    public sealed class UITheme {
        public const string DefaultId = "Default";
        private static Dictionary<string, UITheme> all = new();
        private static UITheme fallback;
        private UIThemeDef def;
        public string Id;
        public string Name;
        public float SelectableFadeTime;
        private bool isBuilt;
        private readonly Dictionary<string, Color> colors = new();
        private readonly Dictionary<string, UIThemeImage> images = new();
        private readonly Dictionary<string, UIThemeText> texts = new();
        private readonly Dictionary<string, UIThemeSelectable> selectables = new();
        private readonly Dictionary<string, UIThemeButton> buttons = new();
        private readonly Dictionary<string, UIThemeDropdown> dropdowns = new();
        private readonly Dictionary<string, UIThemeSlider> sliders = new();
        private readonly Dictionary<string, UIThemeScrollbar> scrollbars = new();
        private readonly Dictionary<string, UIThemeInput> inputs = new();

        public string DisplayName => Id == DefaultId ? T.AdjDefault : Name;

        private static IEnumerable<UITheme> Sorted => all.Values
            .OrderByDescending(v => v.Id == DefaultId).ThenBy(v => v.Name);

        public static List<string> OptionIds => Sorted.Select(v => v.Id).ToList();

        public static List<string> Options => Sorted.Select(v => v.DisplayName).ToList();

        public UIThemeImage GetImage(string id, object ctx) {
            if (id == null) {
                return UIThemeImage.WhiteSquare;
            }
            if (id == "MainMenuGradient") {
                D.Log("Getting main menu gradient image");
            }
            if (TryImageWithFallback(id, out var img)) {
                return img;
            }
            D.Err("Image not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeText GetText(string id, object ctx) {
            if (TryTextWithFallback(id, out var text)) {
                return text;
            }
            D.Err("Text not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeSelectable GetSelectable(string id, object ctx) {
            if (TrySelectableWithFallback(id, out var selectable)) {
                return selectable;
            }
            D.Err("Selectable not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeDropdown GetDropdown(string id, object ctx) {
            if (TryDropdownWithFallback(id, out var dropdown)) {
                return dropdown;
            }
            D.Err("Dropdown not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeSlider GetSlider(string id, object ctx) {
            if (TrySliderWithFallback(id, out var slider)) {
                return slider;
            }
            D.Err("Slider not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeInput GetInput(string id, object ctx) {
            if (TryInputWithFallback(id, out var input)) {
                return input;
            }
            D.Err("Input not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeButton GetButton(string id, object ctx) {
            if (TryButtonWithFallback(id, out var button)) {
                return button;
            }
            D.Err("Button not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public UIThemeScrollbar GetScrollbar(string id, object ctx) {
            if (TryScrollbarWithFallback(id, out var scrollbar)) {
                return scrollbar;
            }
            D.Err("Scrollbar not found: {0} | {1}", id, D.Str(ctx));
            return null;
        }

        public Color GetColor(string idOrHex, object ctx) {
            return GetColor(idOrHex, 1f, ctx);
        }
        public Color GetColor(string idOrHex, float alpha, object ctx) {
            if (alpha == 0f) {
                alpha = 1f;
            }
            if (idOrHex == null) {
                D.Err("Color idOrHex is null! {0}", D.Str(ctx));
                return Color.magenta;
            }
            // Check if already cached
            if (colors.TryGetValue(idOrHex, out var cached)) {
                cached.a = cached.a * alpha;
                return cached;
            }
            if (idOrHex.StartsWith('#')) {
                if (Colors.TryFromHex(idOrHex, out var col)) {
                    col.a = col.a * alpha;
                    return col;
                }
            } else {
                // Check for HSL modifiers (e.g., "Dark+L20-S20")
                var col = TryParseWithHslModifiers(idOrHex, ctx);
                if (col.HasValue) {
                    var c = col.Value;
                    c.a = c.a * alpha;
                    return c;
                }
            }
            D.Err("Color not found: {0} | {1} | Theme: {2}", idOrHex, ctx, Id);
            return Color.magenta;
        }

        private bool TryWithFallback<T>(string id, Dictionary<string, T> coll, Dictionary<string, T> fallbackCol, out T res) {
            if (coll == null) {
                return fallbackCol.TryGetValue(id, out res);
            }
            if (coll.TryGetValue(id, out res)) {
                return true;
            }
            if (coll != fallbackCol) {
                return fallbackCol.TryGetValue(id, out res);
            }
            return false;
        }
        private bool TryImageWithFallback(string id, out UIThemeImage img) {
            return TryWithFallback(id, images, fallback.images, out img);
        }
        private bool TryTextWithFallback(string id, out UIThemeText text) {
            return TryWithFallback(id, texts, fallback.texts, out text);
        }
        private bool TrySelectableWithFallback(string id, out UIThemeSelectable selectable) {
            return TryWithFallback(id, selectables, fallback.selectables, out selectable);
        }
        private bool TryButtonWithFallback(string id, out UIThemeButton button) {
            return TryWithFallback(id, buttons, fallback.buttons, out button);
        }
        private bool TryDropdownWithFallback(string id, out UIThemeDropdown dropdown) {
            return TryWithFallback(id, dropdowns, fallback.dropdowns, out dropdown);
        }
        private bool TrySliderWithFallback(string id, out UIThemeSlider slider) {
            return TryWithFallback(id, sliders, fallback.sliders, out slider);
        }
        private bool TryScrollbarWithFallback(string id, out UIThemeScrollbar scrollbar) {
            return TryWithFallback(id, scrollbars, fallback.scrollbars, out scrollbar);
        }
        private bool TryInputWithFallback(string id, out UIThemeInput input) {
            return TryWithFallback(id, inputs, fallback.inputs, out input);
        }

        private Color? TryParseWithHslModifiers(string idWithModifiers, object ctx) {
            if (idWithModifiers == "Accent2") {
                D.Warn("break ");
            }
            // try the color by id outright
            if (colors.TryGetValue(idWithModifiers, out var col)) {
                return col;
            }
            // Find the first modifier position
            int firstModifierPos = -1;
            for (int i = 0; i < idWithModifiers.Length; i++) {
                if (idWithModifiers[i] == '+' || idWithModifiers[i] == '-') {
                    firstModifierPos = i;
                    break;
                }
            }
            if (firstModifierPos <= 0) {
                return null;
            }
            var baseId = idWithModifiers.Substring(0, firstModifierPos);
            if (!colors.TryGetValue(baseId, out var baseColor)) {
                return null;
            }
            // Convert base color to HSL
            Colors.ToHsl(baseColor, out float h, out float s, out float l);
            float a = baseColor.a;
            // Parse and apply modifiers by splitting on + and -
            var modifiersPart = idWithModifiers.Substring(firstModifierPos);
            int i2 = 0;
            while (i2 < modifiersPart.Length) {
                bool isAdd = modifiersPart[i2] == '+';
                i2++;
                if (i2 >= modifiersPart.Length) break;
                char component = modifiersPart[i2];
                i2++;
                // Parse the number
                int numStart = i2;
                while (i2 < modifiersPart.Length &&
                       modifiersPart[i2] != '+' && modifiersPart[i2] != '-') {
                    i2++;
                }
                if (numStart == i2) continue;
                if (!float.TryParse(modifiersPart.Substring(numStart, i2 - numStart),
                        out float value)) {
                    continue;
                }
                float delta = isAdd ? value : -value;
                switch (component) {
                    case 'H':
                        h = (h + delta + 360f) % 360f;
                        break;
                    case 'S':
                        s = Mathf.Clamp(s + delta, 0f, 100f);
                        break;
                    case 'L':
                        l = Mathf.Clamp(l + delta, 0f, 100f);
                        break;
                    case 'A':
                        a = Mathf.Clamp01(a + delta / 100f);
                        break;
                }
            }
            // Convert back to RGB
            var result = Colors.ToColor(h, s, l);
            result.a = a;
            // Cache the result
            colors[idWithModifiers] = result;
            return result;
        }

        public UITheme(UIThemeDef def) {
            Id = def.Id;
            Name = def.Name ?? Id;
            this.def = def;
        }

        public void Build() {
            if (isBuilt) { return; }
            SelectableFadeTime = def.SelectableFadeTime;
            if (SelectableFadeTime == 0f) {
                SelectableFadeTime = fallback.SelectableFadeTime;
            }
            LoadColors(def);
            LoadImages(def);
            LoadTexts(def);
            LoadSelectables(def);
            LoadButtons(def);
            LoadDropdowns(def);
            LoadSliders(def);
            LoadScrollbars(def);
            LoadInputs(def);
            isBuilt = true;
        }

        private void LoadImages(UIThemeDef def) {
            var coll = def.Images ?? fallback.def.Images;
            foreach (var kv in coll) {
                if (kv.Key == "MainMenuGradient") {
                    D.Warn("Setting menu gradient image");
                }
                kv.Value.SetImage(this, images, kv.Key);
            }
            if (this != fallback) {
                // add all images from fallback that were skipped in the original,
                // otherwise colors will not be overridden
                foreach (var kv in fallback.def.Images) {
                    if (!images.ContainsKey(kv.Key)) {
                        kv.Value.SetImage(this, images, kv.Key);
                    }
                }
            }
        }
        private void LoadTexts(UIThemeDef def) {
            var coll = def.Texts ?? fallback.def.Texts;
            foreach (var kv in coll) {
                kv.Value.SetText(this, texts, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Texts) {
                    if (!texts.ContainsKey(kv.Key)) {
                        kv.Value.SetText(this, texts, kv.Key);
                    }
                }
            }
        }

        private void LoadSelectables(UIThemeDef def) {
            var coll = def.Selectables ?? fallback.def.Selectables;
            foreach (var kv in coll) {
                kv.Value.SetSelectable(this, selectables, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Selectables) {
                    if (!selectables.ContainsKey(kv.Key)) {
                        kv.Value.SetSelectable(this, selectables, kv.Key);
                    }
                }
            }
        }

        private void LoadButtons(UIThemeDef def) {
            var coll = def.Buttons ?? fallback.def.Buttons;
            foreach (var kv in coll) {
                kv.Value.SetButton(this, buttons, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Buttons) {
                    if (!buttons.ContainsKey(kv.Key)) {
                        kv.Value.SetButton(this, buttons, kv.Key);
                    }
                }
            }
        }

        private void LoadDropdowns(UIThemeDef def) {
            var coll = def.Dropdowns ?? fallback.def.Dropdowns;
            foreach (var kv in coll) {
                kv.Value.SetDropdown(this, dropdowns, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Dropdowns) {
                    if (!dropdowns.ContainsKey(kv.Key)) {
                        kv.Value.SetDropdown(this, dropdowns, kv.Key);
                    }
                }
            }
        }

        private void LoadSliders(UIThemeDef def) {
            var coll = def.Sliders ?? fallback.def.Sliders;
            foreach (var kv in coll) {
                kv.Value.SetSlider(this, sliders, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Sliders) {
                    if (!sliders.ContainsKey(kv.Key)) {
                        kv.Value.SetSlider(this, sliders, kv.Key);
                    }
                }
            }
        }

        private void LoadScrollbars(UIThemeDef def) {
            var coll = def.Scrollbars ?? fallback.def.Scrollbars;
            foreach (var kv in coll) {
                kv.Value.SetScrollbar(this, scrollbars, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Scrollbars) {
                    if (!scrollbars.ContainsKey(kv.Key)) {
                        kv.Value.SetScrollbar(this, scrollbars, kv.Key);
                    }
                }
            }
        }

        private void LoadInputs(UIThemeDef def) {
            var coll = def.Inputs ?? fallback.def.Inputs;
            foreach (var kv in coll) {
                kv.Value.SetInput(this, inputs, kv.Key);
            }
            if (this != fallback) {
                foreach (var kv in fallback.def.Inputs) {
                    if (!inputs.ContainsKey(kv.Key)) {
                        kv.Value.SetInput(this, inputs, kv.Key);
                    }
                }
            }
        }

        private void LoadColorsPartial(UIThemeDef src, bool raw, bool isFallback) {
            foreach (var kv in src.Colors) {
                var hexOrId = kv.Value;
                var isRaw = hexOrId.StartsWith('#');
                if (raw != isRaw) { continue; }
                var id = kv.Key;
                // The active theme's own definition always takes precedence over the
                // fallback, regardless of whether it was loaded as raw/alias first.
                if (isFallback && def.Colors.ContainsKey(id)) { continue; }
                if (colors.ContainsKey(id)) { continue; }
                if (hexOrId.StartsWith('#')) {
                    if (Colors.TryFromHex(hexOrId, out var col)) {
                        if (!colors.TryAdd(id, col)) {
                            D.Err("Colors already contains key: {0} | {1} | Theme: {2}",
                                id, col, Id);
                        }
                    } else {
                        D.Err("Failed parsing color: {0} ({1}). Theme: {2}",
                            hexOrId, id, Id);
                    }
                } else {
                    // link to existing color
                    var col = TryParseWithHslModifiers(hexOrId, "LoadColors");
                    if (col.HasValue) {
                        if (!colors.TryAdd(id, col.Value)) {
                            D.Err("Colors already contains key: {0} | {1} | Theme: {2}",
                                id, hexOrId, Id);
                        }
                    } else {
                        D.Err("Couldn't find color by id: {0} ({1}) | Theme: {2}",
                            hexOrId, id, Id);
                    }
                }
            }
        }

        private void LoadColors(UIThemeDef def) {
            def.Colors ??= new();
            // 1. load raw colors from def.Colors
            LoadColorsPartial(def, true, isFallback: false);
            // 2. load missing raw colors from fallback.Colors, skipping any key
            //    the active theme already overrides (even as an alias)
            if (this != fallback) {
                LoadColorsPartial(fallback.def, true, isFallback: true);
            }
            // 3. load all non-raw colors from def.Colors
            LoadColorsPartial(def, false, isFallback: false);
            // 4. load all non-raw colors from fallback.Colors, skipping any key
            //    the active theme already overrides (even as raw hex)
            if (this != fallback) {
                LoadColorsPartial(fallback.def, false, isFallback: true);
            }
        }

        public static UITheme Load() {
            all.Clear();
            var themes = The.ModLoader.LoadObjects<UIThemeDef>(
                "Config/UITheme/Themes", cache: false);
            foreach (var theme in themes) {
                var th = new UITheme(theme);
                if (!all.TryAdd(th.Id, th)) {
                    D.Err("Theme already exists by id: {0}", th);
                } else if (th.Id == DefaultId) {
                    fallback = th;
                }
            }
            // fallback theme must be built first
            fallback.Build();

            // TODO switch
            var themeId = The.Prefs.GetString(Pref.GUITheme, PrefH.GUITheme, DefaultId);
            if (!all.TryGetValue(themeId, out var activeTheme)) {
                activeTheme = all.Values.First();
            }
            activeTheme.Build();
            Texts.CacheAccentedColor(activeTheme);
            return activeTheme;
        }

        public static void ReloadTheme() {
            The.UITheme = Load();
            D.Warn("Reloading Theme: {0}", The.UITheme.Id);
            The.UITheme.Reload();
        }

        public void Reload() {
            foreach (UIThemedImage themed in GameObject.FindObjectsByType<UIThemedImage>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedText themed in GameObject.FindObjectsByType<UIThemedText>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedSelectable themed in GameObject.FindObjectsByType<UIThemedSelectable>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedButton themed in GameObject.FindObjectsByType<UIThemedButton>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedScrollbar themed in GameObject.FindObjectsByType<UIThemedScrollbar>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedDropdown themed in GameObject.FindObjectsByType<UIThemedDropdown>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedSlider themed in GameObject.FindObjectsByType<UIThemedSlider>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UIThemedInput themed in GameObject.FindObjectsByType<UIThemedInput>(
                    FindObjectsInactive.Include)) {
                themed.ForceApplyStyle();
            }
            foreach (UITextCollisionResolver resolver in GameObject.FindObjectsByType<UITextCollisionResolver>(
                    FindObjectsInactive.Include)) {
                resolver.AutoResize();
            }
            The.SysSig.UIThemeReloaded.Send();
        }

        public static void SwitchTo(string id) {
            if (all.TryGetValue(id, out var theme)) {
                The.Prefs.SetString(Pref.GUITheme, theme.Id);
                ReloadTheme();
            } else {
                WPopup.Create(true, null, T.Error, $"Can't set theme by id: {id}")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            }
        }
    }
}
