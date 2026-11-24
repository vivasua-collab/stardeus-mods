using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using System.Linq;
using KL.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Procgen {
    public class ObjectsInSpaceGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        protected readonly List<Def> objects;
        // Behind the engines: see EngineExhaust. Filled by Apply, which subclasses call first
        protected readonly HashSet<int> exhaust = new();

        public ObjectsInSpaceGenStage(string[] things) {
            objects = new();
            foreach (var t in things) {
                var d = The.Defs.TryGet(t);
                if (d != null) {
                    objects.Add(d);
                } else {
                    D.Err("{0} Def not found by id: {1}", LogPrefix, t);
                }
            }
        }

        public ObjectsInSpaceGenStage(List<Def> defs) {
            objects = defs;
        }

        // The legacy placement: anywhere in space next to the ship. The hull stages fall back
        // on it rather than leave a ship without its device
        protected bool PlaceThing(Def thing, bool isFallback) {
            var area = S.Sys.Areas.SolidArea;
            if (spec.HasGenArea) {
                area = area.Where(a => IsWithinGenArea(a)).ToList();
            } else {
                area = new List<int>(area);
            }

            int attempts = 0;
            Arrays.Shuffle(rng, area);
            for (int i = 0; i < area.Count; i++) {
                int posIdx = area[i];
                var facingDir = rng.From(Facing.Types);
                var dir = Facing.ToOffset(facingDir, 1);
                for (int j = 0; j < 4; j++) {
                    if (PlaceThingFacing(thing, ref posIdx, facingDir, dir,
                            isFallback)) {
                        return true;
                    }
                    attempts++;
                    // try rotated
                    facingDir = Facing.Rotate(facingDir, Facing.Type.Right);
                    dir = Facing.ToOffset(facingDir, 1);
                }
            }

            if (!isFallback) {
                D.Warn("Will try to place with fallback: {0}", thing);
                return PlaceThing(thing, true);
            }

            D.Err("Exhausted all options but failed to place {0} in {1} attempts",
                thing, attempts);
            return false;
        }

        private bool PlaceThingFacing(Def thing, ref int posIdx,
                Facing.Type facingDir, Vector2 dir, bool isFallback) {
            do {
                posIdx = Pos.NeighborAt(posIdx, (int)dir.x, (int)dir.y);
                // We're looking for the first non-solid tile here
            } while (!Pos.IsOOB(posIdx) && EntityUtils.IsFloorAt(posIdx));
            if (Pos.IsOOB(posIdx)) { return false; }
            if (!IsWithinGenArea(posIdx)) { return false; }
            bool isTooCloseToEnclosed = false;
            if (!isFallback) {
                Pos.ForEachAround(posIdx, 3, true, true, (pp) => {
                    if (S.Sys.Areas.IsEnclosed(pp)) {
                        isTooCloseToEnclosed = true;
                        return false;
                    }
                    return true;
                });
                if (isTooCloseToEnclosed) { return false; }
            }

            var proto = EntityUtils.CreatePrototype<Tile>(thing);
            if (proto.Transform.HasAnyPort) {
                // particle collector
                proto.Transform.Rotation = Facing.Opposite(facingDir);
                int portPos = Pos.Invalid;
                if (proto.Transform.HasOutPort) {
                    portPos = proto.Transform.RotatedOutPort(posIdx);
                } else if (proto.Transform.HasInPort) {
                    portPos = proto.Transform.RotatedInPort(posIdx);
                } else {
                    portPos = proto.Transform.RotatedWorkSpot(posIdx);
                }
                if (EntityUtils.IsFloorAt(portPos)) {
                    var floor = EntityUtils.FloorAt(portPos);
                    if (!floor.Integrity.CanHaveFullIntegrity) {
                        var newFloor = EntityUtils.CreatePrototype<Tile>(
                            DefIds.StructureFloorsFloor01);
                        floor.GenReplaceWith(S, newFloor);
                    }
                    // good
                    if (EntityUtils.IsWallAt(portPos)) {
                        if (!spec.InvincibleParts.Contains(portPos)) {
                            // D.Warn("Trying to carve a place in the wall for work spot of {0} at {1}",
                            // thing, Pos.String(workSpotPos));
                            new CmdRemoveTile(EntityUtils.WallAt(portPos)).Execute(S);
                        }
                    }
                }
                for (int i = 0; i < proto.Transform.RotatedWidth; i++) {
                    for (int j = 0; j < proto.Transform.RotatedHeight; j++) {
                        var p = Pos.NeighborAt(posIdx, i, j);
                        if (!EngineExhaust.Overlaps(exhaust, proto, p)
                                && CmdPlaceTile.CanPlace(p, proto, WorldLayer.Objects, warn: false)) {
                            // D.Warn("Object with work spot in space gen  success at {0}", Pos.String(p));
                            BuildTile(p, proto);
                            var obj = EntityUtils.ObjAt(p);
                            return obj != null;
                        }
                    }
                }
            } else {
                if (proto.Transform.CellCount > 1) {
                    for (int i = 0; i < proto.Transform.RotatedWidth; i++) {
                        for (int j = 0; j < proto.Transform.RotatedHeight; j++) {
                            var p = Pos.NeighborAt(posIdx, i, j);
                            if (!EngineExhaust.Overlaps(exhaust, proto, p)
                                    && CmdPlaceTile.CanPlace(p, proto, WorldLayer.Objects, warn: false)) {
                                // D.Warn("Multitile in space gen success at {0}", Pos.String(p));
                                BuildTile(p, proto);
                                var obj = EntityUtils.ObjAt(p);
                                return obj != null;
                            }
                        }
                    }
                } else {
                    if (!EngineExhaust.Overlaps(exhaust, proto, posIdx)
                            && CmdPlaceTile.CanPlace(posIdx, proto, WorldLayer.Objects, warn: false)) {
                        //D.Err("Multitile success!");
                        // D.Warn("Single tile in space gen success at {0}", Pos.String(posIdx));
                        BuildTile(posIdx, proto);
                        var obj = EntityUtils.ObjAt(posIdx);
                        return obj != null;
                    }
                }
            }
            // D.Warn("Tile gen failure in space: {0}", thing);
            return false;
        }

        public override void Apply() {
            EngineExhaust.Collect(S, spec, exhaust);
            var things = new List<Def>();

            foreach (var def in objects) {
                if (def == null) {
                } else {
                    things.Add(def);
                }
            }

            var subRng = rng.Fork();
            //things.Sort();
            things = things.OrderBy(s => s.IdH)
                .ThenBy(_ => subRng.Value).ToList();

            foreach (var thing in things) {
                PlaceThing(thing, false);
            }
        }
    }
}
