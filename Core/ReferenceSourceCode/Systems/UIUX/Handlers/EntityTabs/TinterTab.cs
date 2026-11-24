using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class TinterTab : BaseEntityTab {
        public const string TabId = "Tinter";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TinterTab());
        }

        private Tile tile;
        private TinterComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.ObjectsDevicesAutopainter;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<TinterComp>(out var c)) { return false; }
            this.tile = tile;
            comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            comp.UIChanged.Bind(lifetime, ReloadTab, false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            var cap = comp.ColorCapacity;

            var rRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, block.Container, idx++);
            rRow.Load(GraphicsId.ObjMaterialsPigmentRed, T.ObjMaterialsPigmentred,
                cap > 0f ? comp.ColorLevelR / cap : 0f);
            UIUtils.AddCodexTooltip(lifetime, rRow.gameObject, rRow,
                MatType.FastGet(MatIdH.PigmentRed).Def);

            var gRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, block.Container, idx++);
            gRow.Load(GraphicsId.ObjMaterialsPigmentGreen, T.ObjMaterialsPigmentgreen,
                cap > 0f ? comp.ColorLevelG / cap : 0f);
            UIUtils.AddCodexTooltip(lifetime, gRow.gameObject, gRow,
                MatType.FastGet(MatIdH.PigmentGreen).Def);

            var bRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, block.Container, idx++);
            bRow.Load(GraphicsId.ObjMaterialsPigmentBlue, T.ObjMaterialsPigmentblue,
                cap > 0f ? comp.ColorLevelB / cap : 0f);
            UIUtils.AddCodexTooltip(lifetime, bRow.gameObject, bRow,
                MatType.FastGet(MatIdH.PigmentBlue).Def);

            var sliderRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            sliderRow.Item.Load(IconId.CHaul, T.AutoRefillThreshold,
                Units.Percentage(comp.RefillThreshold));
            sliderRow.Slider.Load(comp.RefillThreshold, v => {
                comp.SetRefillThreshold(v);
            }, 0.1f, 1f);
            sliderRow.Slider.SetValueTextFunc(v => Units.Percentage(v));
            UIUtils.AddTip(lifetime, sliderRow, T.AutoRefillThresholdTip);

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime,
                UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => {
                comp.AdjustRefillPriority(+1);
                prio.Texts.SetText2(Units.XOutOfY(comp.RefillPriority, 5));
            });
            prio.Down.Load(IconId.WArrowDown, () => {
                comp.AdjustRefillPriority(-1);
                prio.Texts.SetText2(Units.XOutOfY(comp.RefillPriority, 5));
            });
            prio.Texts.Load(T.RefillPriority, Units.XOutOfY(comp.RefillPriority, 5));
            UIUtils.AddTip(lifetime, prio, T.RefillPriorityHelp);
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
