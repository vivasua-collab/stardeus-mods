using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using System.Collections.Generic;
using Game.Components;
using Game.Commands;
using KL.Randomness;
using Game.CodeGen;
using System.Collections;

namespace Game.Procgen {
    public class OutdoorDeviceGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private string def;
        private string floorDef;
        private int count;
        private bool mirror;
        private float chanceOnboard;
        public OutdoorDeviceGenStage(string def, string floorDef, int count, bool mirror,
                float chanceOnboard) {
            this.def = def;
            this.count = count;
            this.floorDef = floorDef;
            this.mirror = mirror;
            this.chanceOnboard = chanceOnboard;
        }

        public override void Apply() {
            for (int i = 0; i < count; i++) {
                if (mirror || rng.Chance(chanceOnboard) || !TryApply()) {
                    GenerateWeaponAreaOutside(i);
                }
            }
        }

        private bool lastMirrorAttachmentFailed;

        private void GenerateWeaponAreaOutside(int i) {
            // when building mirrored items, we will build two at once
            if (!lastMirrorAttachmentFailed && mirror && i % 2 == 1) {
                return;
            }
            bool wantsMirror = mirror && !lastMirrorAttachmentFailed;
            lastMirrorAttachmentFailed = false;
            var shipBounds = spec.Bounds;
            int startX = rng.Range(shipBounds.MinX + 20, shipBounds.MaxX - 20);
            var startY = shipBounds.MinY - 2;
            var startY2 = shipBounds.MaxY + 2;
            var curX = startX;
            var curY = startY;
            var curY2 = startY2;
            var midY = S.GridHeight / 2;
            while (curX < shipBounds.MaxX) {
                // yield return Viz(Pos.FromXY(curX, curY), 1f);
                if (TryBuildAttachmentAtX(ref curX, ref curY, midY, startY, +1, +1)) {
                    if (wantsMirror) {
                        // yield return Viz(Pos.FromXY(curX, curY2), 1f);
                        if (!TryBuildAttachmentAtX(ref curX, ref curY2, midY, startY2, +1, -1)) {
                            lastMirrorAttachmentFailed = true;
                            D.ErrSoft("Failed to build mirrored def: {0}", def);
                        }
                    }
                    return;
                }
            }
            curX = startX;
            curY = startY;
            curY2 = startY2;
            while (curX > shipBounds.MinX) {
                // yield return Viz(Pos.FromXY(curX, curY), 1f);
                if (TryBuildAttachmentAtX(ref curX, ref curY, midY, startY, -1, +1)) {
                    if (wantsMirror) {
                        // yield return Viz(Pos.FromXY(curX, curY2), 1f);
                        if (!TryBuildAttachmentAtX(ref curX, ref curY2, midY, startY2, -1, -1)) {
                            lastMirrorAttachmentFailed = true;
                            D.ErrSoft("Failed to build mirrored def: {0}", def);
                        }
                    }
                    return;
                }
            }
            StartOver = true;
        }

        private int lastTriedPos;

        private bool TryBuildAttachmentAtX(ref int curX, ref int curY, int midY,
                int startY, int dirX, int dirY) {
            for (int i = 0; i < 2; i++) {
                var offset = dirY > 0 ? 0 : -2;
                var floor = EntityUtils.FloorAt(Pos.FromXY(curX + i, curY + offset));
                if (floor == null) {
                    curY += dirY;
                    var isBadYBelow = dirY > 0 && curY >= midY;
                    var isBadYAbove = dirY < 0 && curY <= midY;
                    if (isBadYBelow || isBadYAbove) {
                        // D.Warn("Bad Y Below/Above: {0}:{1}: {2}/{3}",
                        //     curX, curY, isBadYBelow, isBadYAbove);
                        curY = startY;
                        curX += dirX;
                    }
                    return false;
                }
                // D.Warn("Found floor: {0}", floor);
                if (!floor.Integrity.CanHaveFullIntegrity) {
                    // angled floor, nope
                    curY = startY;
                    curX += dirX;
                    return false;
                }
            }
            // floor is not null, we want to try to build

            // try to build
            // we are on the floor now, attachment is 3x3, so we need to move
            // down by 3 tiles
            int pos = dirY > 0 ? Pos.FromXY(curX, curY - 2) : Pos.FromXY(curX, curY - 1);
            // D.Warn("Trying to build attachment at {0}", Pos.String(pos));
            if (!TryBuildAttachmentAt(pos)) {
                curY = startY;
                curX += dirX;
                lastTriedPos = pos;
                return false;
            }
            return true;
        }

        private bool TryBuildAttachmentAt(int pos) {
            // check if we can build 2x2 in space starting from here
            var rekt = new PosRect(pos, Pos.NeighborAt(pos, 1, 1));
            bool isEmpty = true;
            rekt.ForEach((p) => {
                if (isEmpty && !EntityUtils.IsEmptySpaceAt(p)) {
                    isEmpty = false;
                }
            });
            if (!isEmpty) {
                return false;
            }
            rekt.ForEach((p) => {
                var fl = EntityUtils.CreatePrototype<Tile>(floorDef);
                BuildTile(p, fl);
            });
            var radar = EntityUtils.CreatePrototype<Tile>(def);
            return BuildTile(pos, radar) != null;
        }

        private bool TryApply() {
            var areas = S.Sys.Areas;

            foreach (var area in areas.OuterAreas) {
                var potentialStarts = new HashSet<int>(
                    area.Value.Where(i => IsGoodStart(i)));
                if (TryBuildAt(potentialStarts)) {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildAt(HashSet<int> value) {
            var values = value.OrderBy(v => v)
                .OrderBy(v => rng.Value).ToList();
            for (int i = 0; i < values.Count; i++) {
                int startPos = values[i];
                var proto = EntityUtils.CreatePrototype<Tile>(def);
                proto.Transform.Rotation = Facing.Type.Right;
                var tile = BuildTile(startPos, proto, true);
                if (tile != null) {
                    // Success
                    RepairAround(startPos);
                    S.Clock.ExecuteAfterTicks(Consts.TicksPer5Minutes, () => {
                        if (tile.Flammable.IsOnFire) {
                            if (!S.Sys.Firex.TryExtinguish(tile)) {
                                D.Err("Failed to extinguish the radar");
                            }
                        }
                    });
                    return true;
                }
            }
            return false;
        }

        private void RepairAround(int startingPoint) {
            Pos.ForEachInRadius(startingPoint, 6f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f != null && !f.Health.IsOperational) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
            Pos.ForEachInRadius(startingPoint, 10f, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f != null && !f.Health.IsOperational && rng.Chance(0.5f)) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
        }

        private bool IsGoodStart(int p) {
            bool goodStart1 = true;
            bool goodStart2 = true;

            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    var pos = Pos.NeighborAt(p, x, y);
                    if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) {
                        goodStart1 = false;
                        break;
                    }
                }
                if (!goodStart1) { break; }
            }

            if (goodStart1) { return true; }

            for (int x = -1; x < 2; x++) {
                for (int y = -1; y < 2; y++) {
                    var pos = Pos.NeighborAt(p, x, y);
                    if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) {
                        goodStart2 = false;
                        break;
                    }
                }
                if (!goodStart2) { break; }
            }

            // D.Warn("Good start: {0}", Pos.String(p));
            return goodStart2;
        }
    }
}
