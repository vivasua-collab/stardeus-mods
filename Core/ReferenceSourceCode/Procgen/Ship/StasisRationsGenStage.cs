using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    // Rations for whoever wakes up first in a stasis array: with no survivor area, nothing else
    // feeds the sleeper until EstablishFoodProduction. They lie next to the array's wardrobe,
    // forbidden, so they stay where the sleeper wakes up, and the tutorial asks to allow them.
    //
    // It runs after every other stage on purpose. The player has to find the rations, and each
    // stage after the emergency pod could hide them: loot and starting resources dropped piles
    // on their tile, storages and furniture went over them or right below them, where a tall
    // graphic covers the tile to its north (STA-1787). Placed last, nothing comes after them.
    public class StasisRationsGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        // Every choice below is deterministic, so it needs no rng fork of its own
        public override bool SkipRNG => true;

        private const int SearchRadius = 5;
        private readonly int rations;

        public StasisRationsGenStage(int rations) {
            this.rations = rations;
        }

        public override void Apply() {
            if (Arrays.IsEmpty(spec.StasisPos)) { return; }
            var amt = Mathf.RoundToInt(rations / S.Difficulty.SoftLevel);
            if (amt <= 0) { return; }
            foreach (var arrayPos in spec.StasisPos) {
                var arrayObj = EntityUtils.ParentObjAt(arrayPos);
                if (arrayObj == null
                        || !arrayObj.TryGetComponent<StasisArrayComp>(out var array)) {
                    D.Warn("{0} No StasisArrayComp at {1}", LogPrefix, Pos.String(arrayPos));
                    continue;
                }
                Spawn(S, spec, array, FindWardrobe(array.Tile.CenterIdx), amt, LogPrefix);
            }
        }

        // Also StasisEmergencyPodGenStage's, for a scenario that has it place the rations.
        // Returns where they went, or Pos.Invalid.
        public static int Spawn(GameState s, ShipSpec spec, StasisArrayComp array, Tile wardrobe,
                int amt, string logPrefix) {
            var spot = FindSpot(s, spec, array, wardrobe);
            if (spot == Pos.Invalid) {
                spot = FallbackSpot(array, wardrobe);
                D.Warn("{0} No clear tile for the stasis rations near {1}, using {2}",
                    logPrefix, Pos.String(array.Tile.CenterIdx), Pos.String(spot));
                MoveLooseItemsAside(s, spot);
            }
            var pile = EntityUtils.SpawnRawMaterial(MatId.SurvivalMeal, amt, spot, 0.05f,
                false, false);
            if (pile == null) {
                D.Warn("{0} Could not spawn stasis rations near {1}", logPrefix, Pos.String(spot));
                return Pos.Invalid;
            }
            pile.IsForbidden = true;
            return spot;
        }

        // The array's own room first, then the tile nearest to the wardrobe, ties in position
        // order so the pick never depends on how the tiles were gathered
        private static int FindSpot(GameState s, ShipSpec spec, StasisArrayComp array,
                Tile wardrobe) {
            var center = array.Tile.CenterIdx;
            var near = wardrobe != null ? wardrobe.PosIdx : center;
            var area = s.Sys.Areas.At(center);
            var best = Pos.Invalid;
            var bestDist = int.MaxValue;
            var bestInArea = false;
            Pos.ForEachAround(center, SearchRadius, false, true, (p) => {
                if (!IsInSight(spec, p)) { return true; }
                var inArea = area != 0 && s.Sys.Areas.At(p) == area;
                var dist = Pos.SqrDistance(p, near);
                bool isBetter;
                if (inArea != bestInArea) {
                    isBetter = inArea;
                } else {
                    isBetter = dist < bestDist || (dist == bestDist && p < best);
                }
                if (isBetter) {
                    best = p;
                    bestDist = dist;
                    bestInArea = inArea;
                }
                return true;
            });
            return best;
        }

        private static bool IsInSight(ShipSpec spec, int p) {
            if (Pos.IsOOB(p) || !spec.IsWithinGenArea(p)) { return false; }
            return EntityUtils.IsClearFloorAt(p) && !IsUnderTallGraphic(p);
        }

        // A graphic that HasHeight is drawn one tile taller than its object, over the tile
        // to the north, and a pile there reads as missing. The connectors, the wardrobe and
        // the array itself all have one, and so can whatever the wreck left around the pod.
        private static bool IsUnderTallGraphic(int p) {
            return EntityUtils.ParentObjAt(Pos.S(p))?.Graphics.HasHeight == true;
        }

        // A wreck with no clear tile left around the array: the port of the wardrobe, then of
        // the array, whichever no tall graphic hides
        private static int FallbackSpot(StasisArrayComp array, Tile wardrobe) {
            var arrayPort = array.Tile.Transform.OutPort;
            if (wardrobe != null && wardrobe.Transform.HasOutPort) {
                var port = wardrobe.Transform.OutPort;
                if (!IsUnderTallGraphic(port) || IsUnderTallGraphic(arrayPort)) {
                    return port;
                }
            }
            return arrayPort;
        }

        // A port is walkable, so no tile stands on it, but loose items can lie there
        private static void MoveLooseItemsAside(GameState s, int spot) {
            for (int i = 0; i < 16; i++) {
                var obj = s.Objs.FindFirstInPos(spot);
                if (obj == null) { return; }
                var to = NearestClearTile(spot);
                if (to == Pos.Invalid) { return; }
                var v = EntityUtils.CenterOf(to);
                obj.Transform.TeleportTo(v);
                obj.Graphics.TeleportTo(v);
            }
        }

        private static int NearestClearTile(int from) {
            var best = Pos.Invalid;
            var bestDist = int.MaxValue;
            Pos.ForEachAround(from, SearchRadius, false, true, (p) => {
                if (Pos.IsOOB(p) || !EntityUtils.IsClearFloorAt(p)) { return true; }
                var dist = Pos.SqrDistance(p, from);
                if (dist < bestDist || (dist == bestDist && p < best)) {
                    best = p;
                    bestDist = dist;
                }
                return true;
            });
            return best;
        }

        // The pod's wardrobe: the nearest one to the array
        private Tile FindWardrobe(int center) {
            Tile best = null;
            var bestDist = int.MaxValue;
            Pos.ForEachAround(center, SearchRadius + 2, false, true, (p) => {
                if (Pos.IsOOB(p)) { return true; }
                var obj = EntityUtils.ParentObjAt(p);
                if (obj == null || obj.Definition.IdH != DefIdsH.ObjectsFurnitureWardrobeSteel) {
                    return true;
                }
                var dist = Pos.SqrDistance(p, center);
                if (dist < bestDist) {
                    best = obj;
                    bestDist = dist;
                }
                return true;
            });
            return best;
        }
    }
}
