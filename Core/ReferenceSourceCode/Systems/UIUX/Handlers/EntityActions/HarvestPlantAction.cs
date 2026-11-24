using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class HarvestPlantAction : BaseEntityAction {
        public const string ActionId = "HarvestPlant";

        [SelfInit]
        private static void Register() {
            RegisterAction(new HarvestPlantAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.HarvestNow;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!CanHarvest(tile, out var planter)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, planter), Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!HarvestAll(tiles, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!obj.IsActive || !obj.TryGetComponent<PlantComp>(out var plant) || !plant.CanBeHarvestedNow) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, plant), Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs,
                out EntityActionData data) {
            if (!HarvestAllObjs(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMultiObjs(l, p, i, new List<Obj>(objs), cnt),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, PlanterComp planter) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CLeaf, T.HarvestNow, () => {
                if (Arrays.IsEmpty(planter.CurrentPlants)) {
                    return;
                }
                foreach (var plant in planter.CurrentPlants) {
                    if (plant.CanBeHarvestedNow) {
                        plant.TryOrderHarvest();
                    }
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }
        private WBase Create(Lifetime l, RectTransform p, int i, PlantComp plant) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CLeaf, T.HarvestNow, () => {
                if (!plant.TryOrderHarvest()) {
                    UISounds.PlayActionDenied();
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CLeaf, T.HarvestNow,
                () => HarvestAll(tiles, false, out _));
            return widget;
        }

        private WBase CreateMultiObjs(Lifetime l, RectTransform p, int i,
                List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CLeaf, T.HarvestNow,
                () => HarvestAllObjs(objs, false, out _));
            return widget;
        }

        private static bool HarvestAll(List<Tile> tiles, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanHarvest(p, out var planter)) {
                    c++;
                    if (!dryRun) {
                        foreach (var plant in planter.CurrentPlants) {
                            if (plant.CanBeHarvestedNow) {
                                plant.TryOrderHarvest();
                            }
                        }
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool HarvestAllObjs(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (obj.IsActive && obj.TryGetComponent<PlantComp>(out var plant) && plant.CanBeHarvestedNow) {
                    c++;
                    if (!dryRun) {
                        plant.TryOrderHarvest();
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanHarvest(Tile tile, out PlanterComp planter) {
            planter = null;
            if (!tile.IsActive) {
                return false;
            }
            if (tile.TryGetComponent(out planter)) {
                if (Arrays.IsEmpty(planter.CurrentPlants)) {
                    return false;
                }
                foreach (var pl in planter.CurrentPlants) {
                    if (pl.CanBeHarvestedNow) {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
