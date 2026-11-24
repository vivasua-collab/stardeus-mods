using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TileLabelTab : BaseEntityTab {
        public const string TabId = "TileLabel";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TileLabelTab());
        }

        private Tile tile;
        private TileLabelComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 150;
        public override string TabName => T.Label;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.TryGetComponent<TileLabelComp>(out var c)) { return false; }
            if (!c.CanConfigure) { return false; }
            S = s;
            tile = t;
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var bidx = 0;
            // Block 0: Label input
            var block0 = UISpawn.Block(lifetime, group, bidx++);
            int idx = 0;

            var labelInput = UISpawn.Widget<WInputWithIcon>(lifetime,
                UIPrefabId.WContent2Input, block0.Container, idx++);
            labelInput.Icon.Load(IconId.CLabel);
            labelInput.Input.Load(T.SetLabel, comp.Label, (v) => {
                comp.AfterRename(v);
            });
            labelInput.Input.SetOnSubmit((v) => {
                ReloadTab();
            });

            // Block 1: Note text area
            // var block1 = UISpawn.Block(lifetime, group, 1);
            // idx = 0;

            var noteInput = UISpawn.Widget<WInput>(lifetime,
                UIPrefabId.WContent2TextArea, block0.Container, idx++);
            noteInput.Load(T.NounNote, comp.note, v => {
                comp.note = string.IsNullOrWhiteSpace(v) ? null : v;
                comp.ShowLabelInfo();
                S.Sig.LabelChanged.Send(comp);
            });

            if (comp.Label == null) { return true; }

            // Block 2: Appearance settings
            var block2 = UISpawn.Block(lifetime, group, bidx++);
            idx = 0;

            float rotH = tile.Transform.RotatedHeight;

            var offsetW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block2.Container, idx++);
            offsetW.Item.Load(IconId.CMove, T.Offset, null);
            offsetW.Slider.Load(comp.offsetY, v => {
                comp.offsetY = v;
                comp.UpdatePosition();
            }, -rotH, rotH);
            offsetW.Slider.SetValueTextFunc(v => Units.Num(Mathf.Round(v * 10f) / 10f));

            var sizeW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block2.Container, idx++);
            sizeW.Item.Load(IconId.CMagnifier, T.Size, null);
            sizeW.Slider.Load(comp.size, v => {
                comp.size = v;
                comp.UpdateSize();
            }, 0.7f, 15f);
            sizeW.Slider.SetValueTextFunc(v => Units.Num(Mathf.Round(v * 10f) / 10f));

            var widthW = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block2.Container, idx++);
            widthW.Item.Load(IconId.CMagnifier, T.Width, null);
            widthW.Slider.Load(comp.width, v => {
                comp.width = v;
                comp.UpdateWidth();
            }, 0.1f, 15f);
            widthW.Slider.SetValueTextFunc(v => Units.Num(Mathf.Round(v * 10f) / 10f));

            var colChoice = ColorChoice.ForGeneric(S)
                .WithAction(col => { comp.OnChangeColor(col); ReloadTab(); })
                .IncludeRandom(false);
            var colorW = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block2.Container, idx++);
            colorW.Load(IconId.CColorPreview, null, null, () => colChoice.Show(colorW.Rect));
            RefreshColorWidget(colorW, comp.colorId);

            var removeW = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WContent2ButtonSimple, block2.Container, idx++);
            removeW.Load(Texts.Italic(T.Remove), () => {
                comp.AfterRename(null);
                ReloadTab();
            });

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (comp.Label == null) { return false; }

            int idx = 0;

            var roomNameW = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            roomNameW.Load(T.LabelUseAsRoomName, comp.IsRoomName, _ => {
                comp.ToggleRoomName();
                ReloadTab();
            });

            var alwaysShowW = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            alwaysShowW.Load(T.LabelAlwaysShow, comp.isAlwaysDisplayed, _ => {
                comp.ToggleAlwaysShow();
                ReloadTab();
            });

            return true;
        }

        private static void RefreshColorWidget(WButtonIconText2 w, string colorId) {
            var def = Palette.Get(colorId);
            if (def != null) {
                w.SetImage(IconId.CColorPreview);
                w.SetTint(def.Color);
                w.SetText(def.NameT);
            }
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
