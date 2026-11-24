using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class PlantTab : BaseEntityTab {
        public const string TabId = "Plant";

        [SelfInit]
        private static void Register() {
            RegisterTab(new PlantTab());
        }

        private PlantComp plant;
        private IWildPlant wildPlant;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.Plant;

        public override bool ShowFor(GameState s, ObjData obj) {
            S = s;
            if (obj.Obj == null) {
                return false;
            }
            if (obj.Obj.TryGetComponent<PlantComp>(out var p)) {
                plant = p;
                wildPlant = null;
                return true;
            }
            if (obj.Obj.TryGetComponent<IWildPlant>(out var f)) {
                wildPlant = f;
                plant = null;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            if (plant != null) {
                return LoadPlantContent(group);
            }
            if (wildPlant != null) {
                return LoadWildPlantContent(group);
            }
            return false;
        }

        public override bool LoadFooter(WGroup group) {
            if (plant == null) { return false; }
            if (plant.PlanterId == 0) { return false; }
            var planter = S.FindEntity<Tile>(plant.PlanterId);
            if (planter == null) { return false; }
            var btn = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WContent4Button, group.Container, 0);
            btn.Load(planter.Definition.Preview, planter.Definition.NameT, null,  () => {
                EntityUtils.SelectOrFocus(planter);
            });
            var tip = UIUtils.AddTip(lifetime, btn, T.Select);
            tip.HoverFun = (h) => {
                EntityUtils.ShowEntityLink(null, planter, h);
            };
            return true;
        }

        private static string GrowthLabel(bool isGrowing, float growth) {
            if (growth >= 1f) {
                return T.FullyGrown;
            }
            return isGrowing ? T.Growing : T.NotGrowing;
        }

        private bool LoadPlantContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            header.Label.Load(GrowthLabel(plant.IsGrowing, plant.Growth));
            header.Progress.SetPercent(plant.Growth, false);
            header.Progress.SetText(Units.Percentage(plant.Growth));

            plant.GrowthB.Bind(lifetime, (g) => {
                header.Progress.SetPercent(g, false);
                header.Progress.SetText(Units.Percentage(g));
            }, false);

            plant.StateChangedB.Bind(lifetime, () => {
                header.Label.Load(GrowthLabel(plant.IsGrowing, plant.Growth));
            }, false);

            var temp = S.GetTemp(plant.Obj.PosIdx);
            var tempRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            tempRow.Load(IconId.CTemperature,
                T.TempRange_V2.F(
                    Texts.Temperature(plant.MinTemp),
                    Texts.Temperature(plant.MaxTemp)),
                Texts.Temperature(temp));

            int gIdx = 1;
            var harvestBlock = UISpawn.Block(lifetime, group, gIdx++);
            int hIdx = 0;

            var harvestHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, harvestBlock.Container, hIdx++);
            harvestHeader.Load(T.HarvestOutput);

            foreach (var mat in plant.HarvestOutput) {
                var matRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, harvestBlock.Container, hIdx++);
                matRow.Load(mat.Type.IconId,
                    mat.Type.NameT,
                    $"x{mat.StackSize}");
                UIUtils.AddCodexTooltip(lifetime, matRow.gameObject, matRow, mat.Type.Def);
            }

            if (A.InstantBuild) {
                var devBlock = UISpawn.Block(lifetime, group, gIdx++);
                int dIdx = 0;
                var devHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, devBlock.Container, dIdx++);
                devHeader.Load("Dev: Growth");
                var slider = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, devBlock.Container, dIdx++);
                slider.Item.Load(IconId.CPercentGreen, "Set Growth Percent",
                    Units.Percentage(plant.Growth));
                slider.Slider.Load(plant.Growth, (v) => {
                    plant.DevSetGrowth(v);
                    ReloadTab();
                });
            }

            return true;
        }

        private bool LoadWildPlantContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            header.Label.Load(GrowthLabel(wildPlant.IsGrowing, wildPlant.Growth));
            header.Progress.SetPercent(wildPlant.Growth, false);
            header.Progress.SetText(Units.Percentage(wildPlant.Growth));

            wildPlant.GrowthB.Bind(lifetime, (g) => {
                header.Progress.SetPercent(g, false);
                header.Progress.SetText(Units.Percentage(g));
            }, false);

            wildPlant.StateChangedB.Bind(lifetime, () => {
                header.Label.Load(GrowthLabel(wildPlant.IsGrowing, wildPlant.Growth));
            }, false);

            return true;
        }

        protected override void Cleanup() {
            plant = null;
            wildPlant = null;
            S = null;
        }
    }
}
