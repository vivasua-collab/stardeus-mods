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
    public class ShuttleGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private readonly int numShuttles;
        private readonly bool tryAddExitZone;
        private readonly bool skipZoneIfOutside;
        private Facing.Type facing;

        public ShuttleGenStage(int num, bool tryAddExit, bool skipZoneIfOutside) {
            numShuttles = num;
            this.tryAddExitZone = tryAddExit;
            this.skipZoneIfOutside = skipZoneIfOutside;
        }

        public override void Apply() {
            // Try to find surviving stasis arrays first
            if (S.Components.CountOfConstructed<ShuttleComp>() >= numShuttles) {
                return;
            }
            facing = rng.OneOf(Facing.Type.Down, Facing.Type.Right, Facing.Type.Up);

            if (!TryApply(facing)) {
                if (facing == Facing.Type.Right) {
                    facing = Facing.Type.Down;
                } else {
                    facing = Facing.Type.Right;
                }
                D.Warn("Fallback shuttle with facing {0}", facing);
                if (!TryApply(facing)) {
                    GenerateShuttleAreaOutside();
                }
            }
        }

        private void GenerateShuttleAreaOutside() {
            var shipBounds = spec.Bounds;
            int startX = rng.Range(shipBounds.MinX + 20, shipBounds.MaxX - 20);
            var startY = shipBounds.MinY - 2;
            var curX = startX;
            var curY = startY;
            var maxY = S.GridHeight / 2;
            while (curX < shipBounds.MaxX) {
                if (TryBuildShuttleAttachmentAtX(ref curX, ref curY, maxY, startY, +1, out var triedPos)) {
                    return;
                }
            }
            curX = startX;
            curY = startY;
            while (curX > shipBounds.MinX) {
                if (TryBuildShuttleAttachmentAtX(ref curX, ref curY, maxY, startY, -1, out var triedPos)) {
                    return;
                }
            }
            StartOver = true;
        }

        private bool TryBuildShuttleAttachmentAtX(ref int curX, ref int curY, int maxY, int startY, int dir, out int triedPos) {
            for (int i = 0; i < 4; i++) {
                var floor = EntityUtils.FloorAt(Pos.FromXY(curX + i, curY));
                if (floor == null) {
                    curY++;
                    if (curY >= maxY) {
                        curY = startY;
                        curX += dir;
                    }
                    triedPos = -1;
                    return false;
                }
                D.Warn("Found floor: {0}", floor);
                if (!floor.Integrity.CanHaveFullIntegrity) {
                    // angled floor, nope
                    curY = startY;
                    curX += dir;
                    triedPos = -1;
                    return false;
                }
            }
            // floor is not null, we want to try to build

            // try to build
            // we are on the floor now, attachment is 4x4, so we need to move
            // down by 4 tiles
            int pos = Pos.FromXY(curX, curY - 4);
            triedPos = pos;
            D.Warn("Trying to build attachment at {0}", Pos.String(triedPos));
            if (!TryBuildShuttleAttachmentAt(pos)) {
                curY = startY;
                curX += dir;
                return false;
            }
            return true;
        }

        private bool TryBuildShuttleAttachmentAt(int pos) {
            // check if we can build 4x4 in space starting from here
            var rekt = new PosRect(pos, Pos.NeighborAt(pos, 3, 3));
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
                var fl = EntityUtils.CreatePrototype<Tile>(DefIds.StructureFloorsFloorReinforced03);
                BuildTile(p, fl);
            });
            return TryBuildTemplate(pos, The.Defs.FastGet(DefIdsH.TemplatesAttachedShuttleBottom));
        }

        private bool TryApply(Facing.Type facing) {
            var shuttlesLeftToBuild = numShuttles;

            var areas = S.Sys.Areas;

            // All islands that are non-cpu ones
            var islands = areas.Islands.OrderBy(i => i.Value.Count)
                .OrderByDescending(i => rng.Value).ToList();

            if (islands.Count == 0) {
                // We are probably not in a wrecked ship
                islands = areas.Islands.ToList();
            }

            int startingPoint = Pos.Invalid;

            foreach (var startingIsland in islands) {
                var startingPosRect = Pos.Encapsulate(startingIsland.Value);

                var areasInStartingIsland = areas.Areas.Where(a =>
                    // only areas bigger than 20 tiles
                    a.Value.Count > 20 &&
                    areas.IslandAt(a.Value.First()) == startingIsland.Key)
                        // the room RoomGrowthGenStage reserved for the shuttle first
                        .OrderByDescending(a => GenUtils.ContainsAny(a.Value, spec.ShuttleRoomHints))
                        .ThenByDescending(r => rng.Value);

                foreach (var area in areasInStartingIsland) {
                    var potentialStarts = new HashSet<int>(
                        area.Value.Where(i => IsGoodStart(i)));
                    if (TryBuildShuttleAt(potentialStarts)) {
                        if (--shuttlesLeftToBuild < 1) {
                            return true;
                        }
                    }
                }

                // There were no surviving areas in the island, or we couldn't build
                var startingIslandOrdered = new List<int>(startingIsland.Value);
                startingIslandOrdered.Sort();

                // Must have .ToList() or OrderBy(p => rng.value).FirstOrDefault(..)
                // will return unpredictable result
                var starts = startingIslandOrdered.OrderBy(p => rng.Value).ToList()
                    .Where(p => IsGoodStart(p)).ToList();

                if (starts.Count == 0) { continue; }

                foreach (var start in starts) {
                    if (TryBuildShuttleAt(new() { start })) {
                        if (--shuttlesLeftToBuild < 1) {
                            return true;
                        }
                    }
                }
            }

            // We failed to build a shuttle
            #if !PRODUCTION_BUILD
            The.ScreenBlock.HideImmediate();
            //D.Break();
            #endif

            return false;
        }

        private bool TryBuildShuttleAt(HashSet<int> value) {
            var values = value.OrderBy(v => v)
                .OrderBy(v => rng.Value).ToList();
            for (int i = 0; i < values.Count; i++) {
                int startPos = values[i];
                var shuttleProto = EntityUtils.CreatePrototype<Tile>(
                            DefIds.ObjectsDevicesShuttle);
                shuttleProto.Transform.Rotation = facing;
                var shuttleTile = BuildTile(startPos, shuttleProto, true);
                if (shuttleTile != null) {
                    // Success
                    GenUtils.MoveBeingsToOutPort(S, shuttleTile);
                    RepairAround(startPos);
                    // Make sure the shuttle survives
                    S.Clock.ExecuteAfterTicks(Consts.TicksPer5Minutes, () => {
                        if (shuttleTile.Flammable.IsOnFire) {
                            if (!S.Sys.Firex.TryExtinguish(shuttleTile)) {
                                D.Err("Failed to extinguish the shuttle");
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

        public static void ResolveShuttleZone(int shutPos, Facing.Type rot,
                out int zoneW, out int zoneH, out int zoneStart) {
            zoneStart = Pos.SW(shutPos);
            zoneW = 0;
            zoneH = 0;
            switch (rot) {
                case Facing.Type.Down: {
                        zoneW = 5;
                        zoneH = 6;
                        break;
                    }
                case Facing.Type.Up: {
                        zoneW = 5;
                        zoneH = 6;
                        break;
                    }
                case Facing.Type.Left: {
                        zoneW = 6;
                        zoneH = 5;
                        break;
                    }
                case Facing.Type.Right: {
                        zoneW = 6;
                        zoneH = 5;
                        break;
                    }
            }
            // D.Warn("Resolving shuttle zone for {0}: {1}: {2} ({3}x{4})",
            //     Pos.String(shutPos), rot, Pos.String(zoneStart), zoneW, zoneH);
        }

        private bool IsGoodStart(int p) {
            bool goodStart = true;
            var stasisPos = spec.StasisPos;
            var CPUPos = spec.CPUPos;
            var survivorPos = spec.SurvivorPos;
            if (!Arrays.IsEmpty(stasisPos)) {
                foreach (var sp in stasisPos) {
                    if (Pos.SqrDistance(sp, p) < 100) {
                        return false;
                    }
                    if (sp > 0 && S.Sys.Areas.IsSameAreaIndoors(p, sp)) {
                        return false;
                    }
                    // D.Warn("Stasis is not too close at shuttle position; {0}", Pos.StringFromTo(sp, p));
                }
            }
            if (CPUPos > 0 && S.Sys.Areas.IsSameAreaIndoors(p, CPUPos)) {
                return false;
            }
            if (survivorPos > 0 && S.Sys.Areas.IsSameAreaIndoors(p, survivorPos)) {
                return false;
            }
            ResolveShuttleZone(p, facing, out var sZoneW, out var sZoneH, out var sZoneStart);
            if (!OpenShuttleAreaGenStage.IsZoneEmpty(null, sZoneStart, sZoneW, sZoneH)) {
                return false;
            }
            if (goodStart && tryAddExitZone) {
                if (skipZoneIfOutside && S.Sys.Areas.IsOutside(p)) {
                    return goodStart;
                }
                OpenShuttleAreaGenStage.ResolveShuttleExitZone(p, facing,
                    out var zoneW, out var zoneH, out var zoneStart, true);
                if (!OpenShuttleAreaGenStage.IsZoneEmpty(
                        S.Sys.Areas.AnyAreaAt(p), zoneStart, zoneW, zoneH)) {
                    return false;
                }
            }
            // D.Warn("Good start: {0}", Pos.String(p));
            return goodStart;
        }
    }
}
