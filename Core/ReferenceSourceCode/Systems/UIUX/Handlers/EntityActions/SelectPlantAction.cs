using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SelectPlantAction : BaseEntityAction {
        public const string ActionId = "SelectPlant";

        [SelfInit]
        private static void Register() {
            RegisterAction(new SelectPlantAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.SelectPlant;

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!CountSelectable(s, tiles, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Tile>(tiles), cnt),
                Priority.Normal);
            return true;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Tile> tiles, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CLeaf, T.SelectPlant,
                () => ShowPlantChoice(s, tiles));
            return widget;
        }

        private static void ShowPlantChoice(GameState s, List<Tile> tiles) {
            var planters = new List<PlanterComp>();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanSelect(s, p) && p.TryGetComponent<PlanterComp>(out var planter)) {
                    planters.Add(planter);
                }
            }
            if (planters.Count == 0) {
                return;
            }

            var plants = PlanterComp.AvailablePlants;
            if (plants.Count == 0) {
                return;
            }

            var qc = QuickChoice.Create(T.SelectPlant);
            for (int pi = 0; pi < plants.Count; pi++) {
                var plant = plants[pi];
                if (!s.Research.IsDefUnlocked(plant.Def)) {
                    continue;
                }
                int compatible = CountCompatible(planters, plant);
                if (compatible == 0) {
                    continue;
                }
                var pl = plant;
                qc.AddButton($"{plant.PlantName} ({Units.XNum(compatible)})", () => {
                    StartGrowing(planters, pl);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            qc.Show();
        }

        private static int CountCompatible(List<PlanterComp> planters, PlantComp plant) {
            int count = 0;
            for (int i = 0; i < planters.Count; i++) {
                if (planters[i].CanGrow(plant)) {
                    count++;
                }
            }
            return count;
        }

        private static void StartGrowing(List<PlanterComp> planters, PlantComp plant) {
            for (int i = 0; i < planters.Count; i++) {
                if (planters[i].Entity.IsActive) {
                    planters[i].StartGrowing(plant);
                }
            }
        }

        private static bool CountSelectable(GameState s, List<Tile> tiles, out int cnt) {
            int c = 0;
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (CanSelect(s, p)) {
                    c++;
                }
            }
            cnt = c;
            return cnt > 0;
        }

        private static bool CanSelect(GameState s, Tile tile) {
            if (!tile.IsActive || !tile.IsConstructed) {
                return false;
            }
            if (!tile.TryGetComponent<PlanterComp>(out var planter)) {
                return false;
            }
            if (planter.IsConfigured) {
                return false;
            }
            return planter.HasUnlockedPlant;
        }
    }
}
