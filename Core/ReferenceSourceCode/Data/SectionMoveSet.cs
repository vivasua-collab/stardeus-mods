#if !PRODUCTION_BUILD
//#define DEBUG_WINCH
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using KL.Grid;
using KL.Utils;

namespace Game.Data {
    // Builds the full set of grid positions that move together with a ship section:
    // the floor island plus every transitively-attached island-0 space cell (angled
    // floors, space-built devices, chained attachments). Shared by CmdMoveSectionBase
    // (winch/push), CmdRotateSection and the local-maneuvering preview
    // (ShipEngineComp.BeginMoveSection) so none of them can compute a different
    // moving set than the others.
    public static class SectionMoveSet {
        public struct Failure {
            public bool Failed;
            // Pos.Invalid when the failure has no single offending position.
            public int ObstaclePos;
        }

        // Reusable state for the transitive space-object walk.
        private static readonly Queue<int> spaceFrontier = new();
        private static readonly HashSet<int> spaceVisited = new();
        private static readonly HashSet<int> islandBoundaryTemp = new();
        // Positions seeded directly from the island's boundary (the single ring
        // of cells touching the island). Constructed island-0 floors only ever
        // travel when they're in this ring - a chain of angled floors bridging
        // toward another section must not pull the whole chain along.
        private static readonly HashSet<int> firstRing = new();
        private static readonly HashSet<int> chainVisited = new();

        private static void EnqueueSpaceNeighbor(int pos, HashSet<int> res) {
            if (Pos.IsOOB(pos) || res.Contains(pos) || spaceVisited.Contains(pos)) {
                return;
            }
            spaceFrontier.Enqueue(pos);
        }

        private static void EnqueueSpaceNeighbors(int pos, HashSet<int> res) {
            EnqueueSpaceNeighbor(Pos.N(pos), res);
            EnqueueSpaceNeighbor(Pos.E(pos), res);
            EnqueueSpaceNeighbor(Pos.W(pos), res);
            EnqueueSpaceNeighbor(Pos.S(pos), res);
            EnqueueSpaceNeighbor(Pos.NE(pos), res);
            EnqueueSpaceNeighbor(Pos.NW(pos), res);
            EnqueueSpaceNeighbor(Pos.SE(pos), res);
            EnqueueSpaceNeighbor(Pos.SW(pos), res);
        }

        // Chains through PlacementConstraint.InSpaceAttachedChained attachments
        // (e.g. solar panel strings) and adds every linked tile straight into res,
        // enqueueing neighbors only for newly-added cells so the outer walk can
        // continue past the last chain link.
        private static void AddAttachedRecursive(Tile tile, HashSet<int> res) {
            if (!chainVisited.Add(tile.PosIdx)) { return; }
            if (tile.Transform.IsMulti) {
                var posCount = tile.Transform.GetSubtilePos(Caches.PosIdx);
                for (int i = 0; i < posCount; i++) {
                    if (res.Add(Caches.PosIdx[i])) {
                        EnqueueSpaceNeighbors(Caches.PosIdx[i], res);
                    }
                }
            } else if (res.Add(tile.PosIdx)) {
                EnqueueSpaceNeighbors(tile.PosIdx, res);
            }
            foreach (var pos in tile.Transform.Outline) {
                var obj = EntityUtils.ParentObjAt(pos);
                if (obj?.Constructable.Placement ==
                        PlacementConstraint.InSpaceAttachedChained) {
                    AddAttachedRecursive(obj, res);
                }
            }
        }

        /// <summary>
        /// Returns the full moving set (island plus attached space objects/floors).
        /// <paramref name="strictWorkSpot"/> controls whether an attached object's work
        /// spot/in-port landing on a different, already-built island is a hard failure
        /// (rotation, which can't proceed with a split work spot) or just skips that one
        /// object (winch/preview, where island 0 facing another not-yet-built device is
        /// the normal case).
        /// </summary>
        public static HashSet<int> Build(GameState s, int thisIsleId, HashSet<int> island,
                bool strictWorkSpot, out Failure failure) {
            failure = new Failure { ObstaclePos = Pos.Invalid };
            var res = new HashSet<int>(island);
            var areas = s.Sys.Areas;

            spaceFrontier.Clear();
            spaceVisited.Clear();
            islandBoundaryTemp.Clear();
            chainVisited.Clear();
            firstRing.Clear();
            PosRect.InnerOutlineOf(island, islandBoundaryTemp);
            foreach (var b in islandBoundaryTemp) {
                EnqueueSpaceNeighbors(b, res);
            }
            foreach (var f in spaceFrontier) {
                firstRing.Add(f);
            }

            try {
                while (spaceFrontier.Count > 0) {
                    var i = spaceFrontier.Dequeue();
                    if (!spaceVisited.Add(i)) { continue; }
                    var obj = EntityUtils.ParentObjAt(i);
                    if (obj == null) {
                        if (EntityUtils.IsFloorAt(i)) {
                            var floorIsle = areas.IslandAt(i);
                            // A floor belonging to a different section must not be
                            // pulled into the moving set, or relocating overlaps it
                            // and destroys a tile.
                            if (floorIsle != 0 && floorIsle != thisIsleId) { continue; }
                            if (EntityUtils.FloorAt(i).IsConstructed) {
                                // Constructed angled/space floors without hull
                                // integrity only travel one layer deep - a chain of
                                // them bridging toward another section must stay put
                                // beyond the first link.
                                if (firstRing.Contains(i)) {
                                    res.Add(i);
                                }
                                continue;
                            }
                            // Unbuilt (blueprint) floor: travels with the section and
                            // keeps the walk going past it.
                            res.Add(i);
                            EnqueueSpaceNeighbors(i, res);
                        }
                        continue;
                    }
                    // Space-built devices always move with the section; on-floor
                    // blueprints (e.g. an unbuilt Radar over an unbuilt floor) move
                    // with it too when attached, so they don't read as a collision.
                    // Constructed on-floor objects at island 0 are unexpected and
                    // left alone.
                    if (!obj.Constructable.IsBuiltInSpace && obj.IsConstructed) { continue; }
                    var objIsle = areas.IslandAt(obj.PosIdx);

                    if (objIsle == thisIsleId) {
                        if (obj.IsConstructed) {
                            D.Err("Object isle is the same as this isle! unexpected: {0} at {1}",
                                obj, Pos.String(obj.PosIdx));
                            continue;
                        }
                        // Straddling blueprint: most of its footprint (including this
                        // root) already sits on the section's own floor; only reached
                        // here via one of its other subtile cells on still-unbuilt
                        // (island-0) floor. Fall through so the whole object - all its
                        // subtiles - gets folded into the moving set below.
                    }

                    // Island 0 is the expected state for a port/work-spot facing
                    // another not-yet-built device - only a genuinely different,
                    // already-built island is a real problem.
                    if (obj.Transform.HasWorkSpot) {
                        var wsIsland = areas.IslandAt(obj.Transform.WorkSpot);
                        if (wsIsland > 0 && wsIsland != thisIsleId) {
                            D.Err("Failing to move, adjacent object work spot is on different island: {0}", obj);
                            if (strictWorkSpot) {
                                failure.Failed = true;
                                break;
                            }
                            continue;
                        }
                    }
                    if (obj.Transform.HasInPort) {
                        var wsIsland = areas.IslandAt(obj.Transform.InPort);
                        if (wsIsland > 0 && wsIsland != thisIsleId) {
                            D.Err("Failing to move, adjacent object in port is on different island: {0}", obj);
                            continue;
                        }
                    }
                    if (obj.Transform.HasOutPort) {
                        var wsIsland = areas.IslandAt(obj.Transform.OutPort);
                        if (wsIsland > 0 && wsIsland != thisIsleId) {
                            D.Err("Failing to move, adjacent object out port is on different island: {0}", obj);
                            continue;
                        }
                    }

                    if (objIsle > 0 && objIsle != thisIsleId) {
                        failure.Failed = true;
                        D.Err("Outline touching object that belongs to another isle: {0}", obj);
                        break;
                    }

                    if (obj.Constructable.Placement == PlacementConstraint.InSpaceAttachedChained) {
                        AddAttachedRecursive(obj, res);
                    }

                    // Check if space object is touching more than one island
                    // FIXME handle a case where solar panel is touching diagonally
                    // to another section and we can't move because of that
                    var outlineIsland = 0;
                    foreach (var op in obj.Transform.Outline) {
                        var opi = areas.IslandAt(op);
                        if (opi == 0) { continue; }
                        if (outlineIsland == 0) {
                            outlineIsland = opi;
                        } else if (outlineIsland != opi) {
                            #if DEBUG_WINCH
                            D.Warn("Found two islands around {0}: {1}",
                                Pos.String(op), obj);
                            #endif
                            failure.Failed = true;
                            failure.ObstaclePos = obj.PosIdx;
                            break;
                        }
                    }
                    if (obj.Transform.IsMulti) {
                        int posCount = obj.Transform.GetSubtilePos(Caches.PosIdx);
                        for (int j = 0; j < posCount; j++) {
                            var sp = Caches.PosIdx[j];
                            if (res.Add(sp)) {
                                EnqueueSpaceNeighbors(sp, res);
                            }
                        }
                    } else {
                        res.Add(i);
                        EnqueueSpaceNeighbors(i, res);
                    }
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed attaching space objects while moving section");
            }
            return res;
        }
    }
}
