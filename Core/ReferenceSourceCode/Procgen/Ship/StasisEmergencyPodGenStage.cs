using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    // Wires a small emergency power cluster into every stasis array the ship generated:
    // a Solar Panel Array on the hull, one or two Big Energy Connectors, and a wardrobe stocked
    // with something to wear. Rations for whoever wakes up first come from
    // StasisRationsGenStage, last, unless a scenario passes them here.
    //
    // Two reasons this exists, both about the opening hours of a game:
    //
    // 1. An unpowered stasis array bleeds stability fast enough that a colonist woken a
    //    week in comes out crippled, or hostile enough to raid the colony. Keeping the
    //    array on local power turns the sleepers from a countdown into a resource, which
    //    is what lets the wake-up be delayed until the player has somewhere to put them.
    // 2. It is a second, self-contained power island, so the player still has to learn to
    //    daisy-chain connectors out to it - the lesson the survivor area used to teach in
    //    the scenarios that have one.
    //
    // Solar, not a reactor: a Matter Reactor here got in the way of ProvideChargeStation,
    // the first tutorial quest, which is about the player building, linking and fuelling a
    // reactor of their own. For the same reason the connectors are Big Energy Connectors and
    // not the Energy Connector that quest asks for, and not Floor Sockets either, whose reach
    // cannot get from an array to the hull. A panel only goes into open space against the
    // hull (InSpaceAttachedChained), which is why it can end up a way off from the array.
    //
    // It runs after destruction, not next to StasisAreaGenStage. Everything it builds is
    // invincible, and destruction draws its rng only for the tiles that are not, so a pod
    // built first steers the shape of the wreck - a panel out on the hull would reshape the
    // curated Quick Start seeds. Island floating is the last thing that moves sections, so
    // after it no link can be pulled out of range. NewBeginningScenario keeps an
    // RngSlotGenStage where this used to run, and SkipRNG keeps it from shifting the stages
    // after it.
    //
    // It deliberately does NOT reach the ship computer's grid: connecting it is the
    // player's job. StasisAreaGenStage keeps the array itself at least minDistToCPU tiles
    // away and out of the CPU's own room, but that says nothing about where in the ring
    // around it this stage puts the pod, so the CPU guards below are the stage's own.
    [MustGoAfterGenStage(typeof(IslandFloatV2GenStage))]
    // RepairAround would patch the floor holes the breach vents an inner room through
    [MustGoBeforeGenStage(typeof(BreachStasisRoomGenStage))]
    public class StasisEmergencyPodGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        // Every choice below is deterministic, so it needs no rng fork of its own
        public override bool SkipRNG => true;

        private const int SearchRadius = 5;
        // Connector, wardrobe, rations. Below this the CPU guard has starved the pod and is
        // dropped, and then floor is laid, rather than losing pieces of it.
        private const int MinCandidates = 3;

        private static readonly string[] WardrobeContents = new string[] {
            DefIds.ObjEquipmentClothingUniform01,
            DefIds.ObjEquipmentClothingUniform02,
            DefIds.ObjEquipmentClothingSpaceSuit01,
            DefIds.ObjEquipmentHatsSpaceHelmet01,
        };

        private readonly int rations;
        private Tile arrayTile;
        private readonly List<int> candidates = new();
        private readonly List<int> built = new();
        private readonly List<int> connSpots = new();
        private readonly List<Vector2> connCenters = new();
        private readonly List<int> panelRoots = new();
        private readonly List<Vector2> panelCenters = new();
        private readonly List<int> hullRoots = new();
        private readonly List<Vector2> hullCenters = new();
        private readonly HashSet<int> exhaust = new();

        public StasisEmergencyPodGenStage(int rations = 0) {
            this.rations = rations;
        }

        public override void Apply() {
            if (Arrays.IsEmpty(spec.StasisPos)) {
                D.Warn("{0} No stasis array to attach an emergency pod to", LogPrefix);
                return;
            }
            EngineExhaust.Collect(S, spec, exhaust);
            foreach (var arrayPos in spec.StasisPos) {
                BuildPodFor(arrayPos);
            }
        }

        private void BuildPodFor(int arrayPos) {
            var arrayObj = EntityUtils.ParentObjAt(arrayPos);
            if (arrayObj == null
                    || !arrayObj.TryGetComponent<StasisArrayComp>(out var array)) {
                D.Warn("{0} No StasisArrayComp at {1}", LogPrefix, Pos.String(arrayPos));
                return;
            }
            // StasisArray is 3x3 and spec stores its bottom left corner
            var center = Pos.NeighborAt(arrayPos, 1, 1);
            arrayTile = array.Tile;
            built.Clear();
            // Before collecting: generation refuses to build on a damaged floor
            RepairAround(center);
            var avoidCPU = true;
            CollectCandidates(center, avoidCPU);
            if (candidates.Count < MinCandidates) {
                D.Warn("{0} Only {1} pod spots away from the CPU near {2}, taking any",
                    LogPrefix, candidates.Count, Pos.String(center));
                avoidCPU = false;
                CollectCandidates(center, avoidCPU);
            }
            if (candidates.Count < MinCandidates) {
                D.Warn("{0} Only {1} pod spots near {2}, laying floor",
                    LogPrefix, candidates.Count, Pos.String(center));
                LayFloor(center, avoidCPU);
            }

            // A missing pod is a balance regression, not a broken ship, so this never
            // sets StartOver - another restart condition would cost generation
            // reliability for no gameplay gain.
            if (TryBuildSolarPower(array, center, avoidCPU,
                    out var panel, out var conn1, out var conn2)) {
                Energize(array, panel, conn1, conn2);
                // The array is powered from here on with life support off, so this is the
                // only stability it can actually hold. StasisAreaGenStage rolled an
                // intact-ship value that would visibly slide instead.
                array.Stability = StasisArrayComp.SettledBasicStability;
            } else {
                D.Warn("{0} Could not place emergency power near stasis array at {1}",
                    LogPrefix, Pos.String(center));
            }

            var wardrobe = TryBuild(DefIds.ObjectsFurnitureWardrobeSteel);
            if (wardrobe != null) {
                StockWardrobe(wardrobe);
            } else {
                D.Warn("{0} Could not place a wardrobe near stasis array at {1}",
                    LogPrefix, Pos.String(center));
            }

            SpawnRations(wardrobe, array);

            MakeAreaInvincible(built, 1);
        }

        private void CollectCandidates(int center, bool avoidCPU) {
            candidates.Clear();
            Pos.ForEachAround(center, SearchRadius, false, true, (p) => {
                if (IsGoodSpot(p, avoidCPU)) {
                    candidates.Add(p);
                }
                return true;
            });
            SortNearestFirst(candidates, center);
        }

        // Only when even dropping the CPU guard left too few spots, which takes a wreck
        // with next to nothing left around the array. Floor goes on space that touches the
        // floor still there, so it extends the array's own island.
        private void LayFloor(int center, bool avoidCPU) {
            var floorDef = EntityUtils.FloorAt(center)?.DefinitionId;
            if (floorDef == null) { return; }
            var spots = new List<int>();
            Pos.ForEachAround(center, SearchRadius, false, true, (p) => {
                if (Pos.IsOOB(p) || !IsWithinGenArea(p)) { return true; }
                if (EntityUtils.IsFloorAt(p) || EntityUtils.IsObjAt(p)) { return true; }
                if (EntityUtils.IsFloorAt(Pos.N(p)) || EntityUtils.IsFloorAt(Pos.S(p))
                        || EntityUtils.IsFloorAt(Pos.E(p))
                        || EntityUtils.IsFloorAt(Pos.W(p))) {
                    spots.Add(p);
                }
                return true;
            });
            SortNearestFirst(spots, center);
            foreach (var p in spots) {
                if (candidates.Count >= MinCandidates) { break; }
                if (BuildAt(p, EntityUtils.CreatePrototype<Tile>(floorDef)) == null) {
                    continue;
                }
                if (IsGoodSpot(p, avoidCPU)) {
                    candidates.Add(p);
                }
            }
            SortNearestFirst(candidates, center);
        }

        // Nearest first, with ties in position order so the pick never depends on how
        // the spots were gathered
        private static void SortNearestFirst(List<int> list, int center) {
            list.Sort((a, b) => {
                var byDist = Pos.SqrDistance(a, center).CompareTo(Pos.SqrDistance(b, center));
                return byDist != 0 ? byDist : a.CompareTo(b);
            });
        }

        private bool IsGoodSpot(int p, bool avoidCPU) {
            if (Pos.IsOOB(p)) { return false; }
            if (!IsWithinGenArea(p)) { return false; }
            if (!EntityUtils.IsEmptyFloorWithIntegrityAt(p)) { return false; }
            // Sharing the starting room makes the pod read as part of the player's own
            // setup rather than as a separate thing they have to reach
            if (avoidCPU && spec.HasCPU
                    && S.Sys.Areas.IsSameAreaIndoors(spec.CPUPos, p)) {
                return false;
            }
            // Sleepers are sent in and woken out through the array's port, and the spots
            // around it are the nearest ones, so without this they fill up first
            var tr = arrayTile.Transform;
            if ((tr.HasInPort && Pos.SqrDistance(p, tr.InPort) <= 2)
                    || (tr.HasOutPort && Pos.SqrDistance(p, tr.OutPort) <= 2)) {
                return false;
            }
            var isOK = true;
            // Blocking a doorway would strand the bots that have to reach the array
            Pos.ForEachAround(p, 1, true, true, (n) => {
                if (GenUtils.IsDoor(n)) {
                    isOK = false;
                    return false;
                }
                return true;
            });
            return isOK;
        }

        private bool TryBuildSolarPower(StasisArrayComp array, int center, bool avoidCPU,
                out Tile panel, out Tile conn1, out Tile conn2) {
            panel = null;
            conn1 = null;
            conn2 = null;
            var connProto = EntityUtils.CreatePrototype<Tile>(
                DefIds.ObjectsDevicesEnergyConnectorBig);
            var panelProto = EntityUtils.CreatePrototype<Tile>(
                DefIds.ObjectsDevicesSolarPanelArray);
            if (connProto?.ENode == null || panelProto?.ENode == null) {
                D.Err("{0} Pod connector or solar panel has no electric node!", LogPrefix);
                return false;
            }
            float range = connProto.ENode.Node.ConnectorRange;

            // A chained connector can be up to range * MaxConnectors from the array. Both
            // lists are filtered after the walk, not inside it: IsGoodSpot and CanPlace walk
            // the grid themselves.
            connSpots.Clear();
            Pos.ForEachInRadius(center, range * StasisPodPowerLayout.MaxConnectors, true,
                    (p) => {
                connSpots.Add(p);
                return true;
            });
            var connLayer = connProto.Transform.LayerId;
            connSpots.RemoveAll(p => !IsGoodSpot(p, avoidCPU)
                || !S.Sys.Areas.IsSameIsland(center, p)
                || !CmdPlaceTile.CanPlace(p, connProto, connLayer, false));
            SortNearestFirst(connSpots, center);
            connCenters.Clear();
            foreach (var p in connSpots) {
                connCenters.Add(EntityUtils.CenterOf(p));
            }

            // ...and a panel one more range further out than the last connector
            panelRoots.Clear();
            Pos.ForEachInRadius(center, range * (StasisPodPowerLayout.MaxConnectors + 1), true,
                    (p) => {
                if (Pos.IsOOB(p) || !IsWithinGenArea(p)) { return true; }
                // Cheap reject for the bulk of the ship, CanPlace does the real check
                if (EntityUtils.IsFloorAt(p)) { return true; }
                panelRoots.Add(p);
                return true;
            });
            var panelLayer = panelProto.Transform.LayerId;
            var panelW = panelProto.Transform.RotatedWidth;
            var panelH = panelProto.Transform.RotatedHeight;
            panelRoots.RemoveAll(p => !TouchesIslandOf(center, p, panelW, panelH)
                || EngineExhaust.Overlaps(exhaust, panelProto, p)
                || !CmdPlaceTile.CanPlace(p, panelProto, panelLayer, false));
            // The same centre TileTransformComp.Position gives a multi-tile device, so the
            // range checks match the ones the grid itself makes
            var panelOffset = new Vector2(panelW / 2f, panelH / 2f);
            var arrayCenter = array.Tile.Position;
            panelRoots.Sort((a, b) => {
                var da = (EntityUtils.ToVector2(a) + panelOffset - arrayCenter).sqrMagnitude;
                var db = (EntityUtils.ToVector2(b) + panelOffset - arrayCenter).sqrMagnitude;
                var byDist = da.CompareTo(db);
                return byDist != 0 ? byDist : a.CompareTo(b);
            });
            panelCenters.Clear();
            foreach (var p in panelRoots) {
                panelCenters.Add(EntityUtils.ToVector2(p) + panelOffset);
            }

            // Against the original hull when that is in reach, so the panel looks like it was
            // part of the ship rather than bolted onto a torn edge (STA-1673)
            hullRoots.Clear();
            hullCenters.Clear();
            for (int i = 0; i < panelRoots.Count; i++) {
                if (IsAgainstHull(panelRoots[i], panelW, panelH)) {
                    hullRoots.Add(panelRoots[i]);
                    hullCenters.Add(panelCenters[i]);
                }
            }
            var roots = hullRoots;
            if (!StasisPodPowerLayout.TryFind(arrayCenter, range, connCenters, hullCenters,
                    out var panelIdx, out var conn1Idx, out var conn2Idx)) {
                roots = panelRoots;
                if (!StasisPodPowerLayout.TryFind(arrayCenter, range, connCenters, panelCenters,
                        out panelIdx, out conn1Idx, out conn2Idx)) {
                    return false;
                }
            }
            panel = BuildAt(roots[panelIdx], panelProto);
            conn1 = BuildAt(connSpots[conn1Idx], connProto);
            if (conn2Idx != -1) {
                conn2 = BuildAt(connSpots[conn2Idx], EntityUtils.CreatePrototype<Tile>(
                    DefIds.ObjectsDevicesEnergyConnectorBig));
            }
            // Every spot passed CanPlace a moment ago, so this is not expected to happen
            if (panel == null || conn1 == null || (conn2Idx != -1 && conn2 == null)) {
                D.Err("{0} Failed building pod power that passed placement checks: {1} {2} {3}",
                    LogPrefix, panel, conn1, conn2);
                return false;
            }
            return true;
        }

        // The pod stays on the array's own island. After SplitStasisAndCPUGenStage the
        // nearest space is often the gap between the two halves, and a panel hung on the
        // far side would sit in the way of the floor the player bridges it with - the same
        // goes for a connector left on the CPU's side of it.
        private bool TouchesIslandOf(int center, int root, int w, int h) {
            Pos.ToXY(root, out var rx, out var ry);
            for (int x = 0; x < w; x++) {
                if (IsIslandFloor(center, rx + x, ry - 1)
                        || IsIslandFloor(center, rx + x, ry + h)) {
                    return true;
                }
            }
            for (int y = 0; y < h; y++) {
                if (IsIslandFloor(center, rx - 1, ry + y)
                        || IsIslandFloor(center, rx + w, ry + y)) {
                    return true;
                }
            }
            return false;
        }

        // The panel's whole inner edge rests on original hull facing north or south
        private bool IsAgainstHull(int root, int w, int h) {
            Pos.ToXY(root, out var rx, out var ry);
            var north = true;
            var south = true;
            for (int x = 0; x < w; x++) {
                north = north && HullRuns.IsHullWallAt(S, spec, Pos.FromXY(rx + x, ry - 1),
                    Facing.Type.Up);
                south = south && HullRuns.IsHullWallAt(S, spec, Pos.FromXY(rx + x, ry + h),
                    Facing.Type.Down);
            }
            return north || south;
        }

        private bool IsIslandFloor(int center, int x, int y) {
            var p = Pos.FromXY(x, y);
            if (Pos.IsOOB(p) || !EntityUtils.IsFloorAt(p)) { return false; }
            return S.Sys.Areas.IsSameIsland(center, p);
        }

        private Tile TryBuild(string defId) {
            for (int i = 0; i < candidates.Count; ) {
                var pos = candidates[i];
                if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) {
                    candidates.RemoveAt(i);
                    continue;
                }
                var tile = BuildAt(pos, EntityUtils.CreatePrototype<Tile>(defId));
                if (tile != null) {
                    candidates.RemoveAt(i);
                    return tile;
                }
                i++;
            }
            return null;
        }

        private Tile BuildAt(int pos, Tile proto) {
            var tile = BuildTile(pos, proto, true);
            if (tile == null) { return null; }
            // Multi-tile devices claim more than the one position we picked
            var cnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int j = 0; j < cnt; j++) {
                built.Add(Caches.PosIdx[j]);
            }
            if (cnt == 0) {
                built.Add(pos);
            }
            return tile;
        }

        private void Energize(StasisArrayComp array, Tile panel, Tile conn1, Tile conn2) {
            Link(array.Tile, conn1);
            var last = conn1;
            if (conn2 != null) {
                Link(conn1, conn2);
                last = conn2;
            }
            Link(last, panel);
            // start producing electricity
            A.Grid.ExecuteTick(G.Ticks);
            // the array reads its power state after this
            A.Grid.ExecuteTick(G.Ticks);
        }

        private void Link(Tile a, Tile b) {
            // Silently dropping these left the array on an unpowered grid, which only shows
            // up as sleepers rotting hours into the game
            if (!A.Grid.TryConnect(a.ENode, b.ENode, out var failReason)) {
                D.Warn("{0} Could not wire the pod's {1} to {2}: {3}",
                    LogPrefix, a, b, failReason);
            }
        }

        private void StockWardrobe(Tile wardrobe) {
            if (!wardrobe.TryGetComponent<ObjStorageComp>(out var storage)) {
                D.Err("{0} Wardrobe has no ObjStorageComp!", LogPrefix);
                return;
            }
            var spawnAt = EntityUtils.CenterOf(wardrobe.PosIdx);
            foreach (var defId in WardrobeContents) {
                var def = The.Defs.TryGet(defId);
                if (def == null) {
                    D.Err("{0} Def not found by id: {1}", LogPrefix, defId);
                    continue;
                }
                var cmd = new CmdSpawnObj(spawnAt, def, true);
                cmd.Execute(S);
                if (cmd.Obj == null) { continue; }
                // On failure the item just stays on the floor by the wardrobe, which is
                // still where the player expects to find it
                storage.Store(null, cmd.Obj.Data, null);
            }
        }

        // Only for a scenario that passes rations here. New Beginning places them in
        // StasisRationsGenStage, last, since the stages after this one could cover them.
        private void SpawnRations(Tile wardrobe, StasisArrayComp array) {
            var amt = Mathf.RoundToInt(rations / S.Difficulty.SoftLevel);
            if (amt <= 0) { return; }
            var spot = StasisRationsGenStage.Spawn(S, spec, array, wardrobe, amt, LogPrefix);
            if (spot != Pos.Invalid) {
                candidates.Remove(spot);
                built.Add(spot);
            }
        }

        private void RepairAround(int center) {
            Pos.ForEachInRadius(center, SearchRadius, true, (p) => {
                var f = EntityUtils.FloorAt(p);
                if (f?.Health.IsOperational == false) {
                    new CmdRepairTile(f, true).Execute(S);
                }
                return true;
            });
        }
    }
}
