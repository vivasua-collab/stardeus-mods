using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    // Opens the room around every stasis array to space. The stasis area is invincible during
    // destruction, so an array that lands in an intact room stays sealed, and the tutorial
    // chain's EncloseStasisArray passes before the player learned anything. Worse, the room
    // can be huge, and one pump never raises the whole room's average oxygen far enough for
    // MakeStasisAreaSurvivable. With the room open, the player plans the walls themselves.
    public sealed class BreachStasisRoomGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        // Enough of a gap that it reads as "open", not as a crack to patch with one wall
        private const int WallsToRemove = 3;
        private const int FloorsToHole = 2;

        private readonly List<int> candidates = new();

        public override void Apply() {
            var arrays = S.Components.GenFind<StasisArrayComp>();
            if (arrays == null) { return; }
            foreach (var array in arrays) {
                var pos = array.Tile.PosIdx;
                if (!IsWithinGenArea(pos)) { continue; }
                Breach(pos);
            }
        }

        private void Breach(int arrayPos) {
            var areas = S.Sys.Areas;
            var areaId = areas.At(arrayPos);
            // Already open to space
            if (areaId <= 0) { return; }

            var outline = new HashSet<int>();
            areas.OutlineOfArea(areaId, outline);
            candidates.Clear();
            foreach (var p in outline) {
                if (!IsWithinGenArea(p)) { continue; }
                if (EntityUtils.WallAt(p) == null) { continue; }
                // Removing an inner wall only merges rooms, which can leave the array in an
                // even bigger sealed room. A wall that touches the room or the outdoors only
                // diagonally leaves the room enclosed with a corner gap, and the enclose step
                // would pass at game start.
                if (!OpensRoomToOutdoors(p, areaId)) { continue; }
                candidates.Add(p);
            }
            if (candidates.Count > 0) {
                SortByDistance(candidates, arrayPos);
                int removed = 0;
                for (int i = 0; i < candidates.Count && removed < WallsToRemove; i++) {
                    var p = candidates[i];
                    spec.InvincibleParts.Remove(p);
                    new CmdRemoveTile(p, WorldLayer.Walls).Execute(S);
                    removed++;
                }
                return;
            }

            // An inner room: vent it through the floor, the way the CPU room is vented
            candidates.AddRange(areas.AreaById(areaId));
            SortByDistance(candidates, arrayPos);
            int holed = 0;
            for (int i = 0; i < candidates.Count && holed < FloorsToHole; i++) {
                var p = candidates[i];
                if (!EntityUtils.IsEmptyFloorWithIntegrityAt(p)) { continue; }
                spec.InvincibleParts.Remove(p);
                new CmdDealDamage(p, WorldLayer.Floor, 90, DamageType.Structural).Execute(S);
                holed++;
            }
            if (holed == 0) {
                D.Err("Could not breach the stasis array room at {0}", Pos.String(arrayPos));
            }
        }

        // Removing this wall joins the room and the outdoors orthogonally, the way the room
        // flood fill connects tiles
        private bool OpensRoomToOutdoors(int wallPos, int areaId) {
            var areas = S.Sys.Areas;
            bool touchesRoom = false;
            bool touchesOutdoors = false;
            CheckSide(areas, Pos.N(wallPos), areaId, ref touchesRoom, ref touchesOutdoors);
            CheckSide(areas, Pos.S(wallPos), areaId, ref touchesRoom, ref touchesOutdoors);
            CheckSide(areas, Pos.E(wallPos), areaId, ref touchesRoom, ref touchesOutdoors);
            CheckSide(areas, Pos.W(wallPos), areaId, ref touchesRoom, ref touchesOutdoors);
            return touchesRoom && touchesOutdoors;
        }

        private static void CheckSide(AreaSys areas, int side, int areaId,
                ref bool touchesRoom, ref bool touchesOutdoors) {
            if (Pos.IsOOB(side)) { return; }
            if (areas.At(side) == areaId) {
                touchesRoom = true;
            } else if (areas.IsOutdoors(side)) {
                touchesOutdoors = true;
            }
        }

        private static void SortByDistance(List<int> list, int from) {
            // Sorted by position first, so equal distances keep a deterministic order
            list.Sort();
            list.Sort((a, b) => Pos.SqrDistance(a, from).CompareTo(Pos.SqrDistance(b, from)));
        }
    }
}
