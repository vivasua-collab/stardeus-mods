using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class LightSourceTab : BaseEntityTab {
        public const string TabId = "LightSource";

        [SelfInit]
        private static void Register() {
            RegisterTab(new LightSourceTab());
        }

        private Tile tile;
        private ObjData obj;
        private LightSourceComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 40;
        public override string TabName => T.Light;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (!t.TryGetComponent<LightSourceComp>(out var c)) { return false; }
            if (!c.IsCopyable) { return false; }
            S = s;
            tile = t;
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.Light);

            if (!comp.IsEmitting) {
                var status = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                status.Load(IconId.CLightbulb, T.Off);
                return true;
            }

            Color.RGBToHSV(comp.MainColor, out float h, out float s, out float l);

            // Range
            var rangeW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            rangeW.Item.Load(IconId.CMove, T.Range, Units.Percentage(comp.CustomRange, false));
            rangeW.Slider.Load(comp.CustomRange, (v) => {
                comp.SetCustomRange(v);
            }, 0.1f, 2f);
            rangeW.Slider.SetValueTextFunc((v) => {
                return Units.Percentage(v, false);
            });

            // Hue — satW is assigned below; captured via closure
            WSliderWithIconText2 satW = null;
            var hueW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            hueW.Item.Load(IconId.WSquare, T.ColorHue, null);
            hueW.Item.SetTint(Color.HSVToRGB(h, 1f, 1f));
            hueW.Slider.Load(h * 360f, (v) => {
                h = v / 360f;
                var c = Color.HSVToRGB(h, s, l);
                c.a = 1f;
                comp.SetColor(c);
                hueW.Item.SetTint(Color.HSVToRGB(h, 1f, 1f));
                if (satW != null) { satW.Item.SetTint(Color.HSVToRGB(h, s, 1f)); }
            }, 0, 360);
            hueW.Slider.SetValueTextFunc((v) => {
                return $"{Mathf.RoundToInt(v)}°";
            });

            // Saturation
            satW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            satW.Item.Load(IconId.WSquare, T.ColorSaturation, null);
            satW.Item.SetTint(Color.HSVToRGB(h, s, 1f));
            satW.Slider.Load(s * 100f, (v) => {
                s = v / 100f;
                var c = Color.HSVToRGB(h, s, l);
                c.a = 1f;
                comp.SetColor(c);
                satW.Item.SetTint(Color.HSVToRGB(h, s, 1f));
            }, 0, 100);
            satW.Slider.SetValueTextFunc((v) => {
                return $"{Mathf.RoundToInt(v)} %";
            });

            // Luminosity
            var lumW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            lumW.Item.Load(IconId.WSquare, T.ColorAlpha, null);
            lumW.Item.SetTint(Color.HSVToRGB(0f, 0f, l));
            lumW.Slider.Load(l * 100f, (v) => {
                l = v / 100f;
                var c = Color.HSVToRGB(h, s, l);
                c.a = 1f;
                comp.SetColor(c);
                lumW.Item.SetTint(Color.HSVToRGB(0f, 0f, l));
            }, 0, 100);
            lumW.Slider.SetValueTextFunc((v) => {
                return $"{Mathf.RoundToInt(v)} %";
            });

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (!comp.IsEmitting) { return false; }
            int idx = 0;

            // Copy color hex button
            var copyW = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, group.Container, idx++);
            copyW.Load(IconId.WSquare, null, null, () => {
                GUIUtility.systemCopyBuffer = Colors.ToHex3(comp.MainColor);
                UISounds.PlayShowDialog();
                ReloadTab();
            });
            comp.ColorB.Bind(lifetime, (c) => {
                var colorHexNow = Colors.ToHex3(comp.MainColor);
                copyW.SetText(T.CopyColorHex_V1.F(colorHexNow));
                copyW.SetTint(new Color(comp.MainColor.r, comp.MainColor.g, comp.MainColor.b, 1f));
            });

            // Paste color hex button (conditional on clipboard content)
            var clipHex = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrWhiteSpace(clipHex)
                    && Colors.ToHex3(comp.MainColor) != clipHex
                    && Colors.TryFromHex(clipHex, out var pasteColor, false)) {
                var pasteW = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent4Button, group.Container, idx++);
                pasteW.Load(IconId.WSquare, T.PasteColorHex_V1.F(clipHex), null, () => {
                    comp.SetColor(pasteColor);
                    UISounds.PlayShowDialog();
                    ReloadTab();
                });
                pasteW.SetTint(new Color(pasteColor.r, pasteColor.g, pasteColor.b, 1f));
            }

            return true;
        }

        protected override void Cleanup() {
            tile = null;
            obj = null;
            comp = null;
            S = null;
        }
    }
}
