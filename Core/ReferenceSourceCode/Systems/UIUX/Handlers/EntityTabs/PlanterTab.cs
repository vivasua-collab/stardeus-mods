using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class PlanterTab : BaseEntityTab {
        public const string TabId = "Planter";

        [SelfInit]
        private static void Register() {
            RegisterTab(new PlanterTab());
        }

        private PlanterComp planter;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 30;
        public override string TabName => T.Planter;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent(out planter)) {
                return false;
            }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            if (planter.IsConfigured) {
                planter.EnsurePlants();
                return LoadConfiguredContent(group);
            }
            return LoadPlantSelection(group);
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            if (!planter.IsConfigured) {
                var ignore = UISpawn.Widget<WToggleText>(lifetime,
                    UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
                ignore.Load(T.DeviceIgnoreUnconfigured, planter.IsIgnoreUnconfiguredOn, isOn => {
                    planter.IsIgnoreUnconfiguredOn = isOn;
                });
                return true;
            }

            var autoHarvest = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            autoHarvest.Load(T.AutoHarvest, planter.IsAutoHarvest, isOn => {
                planter.IsAutoHarvest = isOn;
            });

            var stopButton = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton_Red, group.Container, idx++);
            stopButton.Load(T.StopGrowing, () => {
                planter.StopGrowing();
                ReloadTab();
            });

            return true;
        }

        private bool LoadConfiguredContent(WGroup group) {
            var plants = planter.CurrentPlants;
            if (plants == null || plants.Length == 0) {
                return false;
            }

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            for (int i = 0; i < plants.Length; i++) {
                var plant = plants[i];
                var row = UISpawn.Widget<WButtonIconText2Progress>(lifetime,
                    UIPrefabId.WContent2ProgressButton, block.Container, idx++);
                row.Load(plant.PlantIcon, plant.PlantName,
                    plant.Growth, () => {
                        EntityUtils.SelectOrFocus(plant.Obj);
                    }, Units.Percentage(plant.Growth));
                plant.GrowthB.Bind(lifetime, (g) => {
                    row.SetPercent(g);
                    row.Item.SetText2(Units.Percentage(g));
                });
                UIUtils.AddHover(lifetime, row, (h) => EntityUtils.ShowEntityLink(null, plant.Obj, h));
                UIUtils.AddCodexTooltip(lifetime, row.gameObject, row, plant.Def);
            }

            return true;
        }

        private bool LoadPlantSelection(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.SelectPlant);

            PlanterComp.EnsureAvailablePlants();
            var plants = PlanterComp.AvailablePlants;

            if (plants.Count == 0) {
                var noPlants = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                noPlants.Load(IconId.CWarning, T.NoPlantsAvailable, null);
                return true;
            }

            var count = 0;
            var locked = Caches.DefList;
            locked.Clear();
            for (int i = 0; i < plants.Count; i++) {
                var plant = plants[i];
                if (!planter.CanGrow(plant)) {
                    continue;
                }
                if (!S.Research.IsDefUnlocked(plant.Def)) {
                    locked.Add(plant.Def);
                    continue;
                }
                count++;
                var currentCount = S.Query.ObjCountByDef.Ask(plant.Def);
                var row = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                row.Load(plant.PlantIcon, plant.PlantName,
                    Units.Num(currentCount), () => {
                        planter.StartGrowing(plant);
                        ReloadTab();
                    });
                UIUtils.AddCodexTooltip(lifetime, row.gameObject, row, plant.Def);
            }
            if (count == 0) {
                var none = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                none.Load(IconId.CInfo, T.NotAvailable);
            }
            if (locked.Count > 0) {
                ResearchLockedRow.Spawn(lifetime, block.Container, idx++, locked);
            }

            return true;
        }

        protected override void Cleanup() {
            planter = null;
            S = null;
        }
    }
}
