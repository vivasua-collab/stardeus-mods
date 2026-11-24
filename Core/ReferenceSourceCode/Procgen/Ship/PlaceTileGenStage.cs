using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using System;
using KL.Randomness;
using System.Collections.Generic;

namespace Game.Procgen {
    public class PlaceTileGenStage : BaseShipGenStage {
        private readonly string defId;
        public override bool RequiresAreas => true;
        public delegate int FindPosition(Tile tile, Rng rng, ShipSpec spec,
            int lastBuiltPos, int placedInARow, List<int> availableArea, HashSet<int> usedPositions);
        private readonly FindPosition findPosition;
        private readonly Facing.Type[] rotations;
        private readonly int wantedAmount;
        private readonly int maxInRow;
        private bool mustBeOutdoors;
        private bool mustBeIndoors;
        private bool mustAvoidDoors;
        private bool mustPreservePassages;
        private bool isFallback;
        public PlaceTileGenStage(string defId, int amount, int maxInRow, FindPosition findPosition,
                params Facing.Type[] rotations) {
            this.defId = defId;
            this.wantedAmount = amount;
            this.maxInRow = maxInRow;
            if (findPosition == null) {
                this.findPosition = FindSpotForDevice;
            } else {
                this.findPosition = findPosition;
            }
            this.rotations = rotations;
        }

        public PlaceTileGenStage Indoors() {
            mustBeIndoors = true;
            return this;
        }
        public PlaceTileGenStage Outdoors() {
            mustBeOutdoors = true;
            return this;
        }
        /// <summary>Reject positions whose footprint touches or neighbors a door,
        /// so placed tiles don't block doorways or narrow corridors.</summary>
        public PlaceTileGenStage AvoidDoors() {
            mustAvoidDoors = true;
            return this;
        }
        /// <summary>Reject positions whose footprint would split the surrounding walkable
        /// floor into several pieces (a closed corridor, a sealed pocket). AvoidDoors
        /// only protects doorways; a storage unit placed mid-room can still wall off
        /// the spot another storage has to be worked from. Checked in fallback mode
        /// too: one storage fewer is cheaper than an unreachable one.</summary>
        public PlaceTileGenStage PreservePassages() {
            mustPreservePassages = true;
            return this;
        }

        private int FindSpotForDevice(Tile tile, Rng rng, ShipSpec spec,
                int lastBuiltPos, int placedInARow,
                List<int> availableArea, HashSet<int> usedPositions) {
            if (lastBuiltPos != Pos.Invalid) {
                // try to build horizontal row
                var nEast = Pos.NeighborAt(lastBuiltPos, tile.Transform.RotatedWidth, 0);
                if (!usedPositions.Contains(nEast) && availableArea.Contains(nEast)) {
                    if (IsGoodPosForTile(tile, nEast)) {
                        availableArea.Remove(nEast);
                        return nEast;
                    }
                    // try to build vertical row
                    var nSouth = Pos.NeighborAt(lastBuiltPos, 0, tile.Transform.RotatedHeight);
                    if (!usedPositions.Contains(nSouth) && availableArea.Contains(nSouth)) {
                        if (IsGoodPosForTile(tile, nSouth)) {
                            availableArea.Remove(nEast);
                            return nSouth;
                        }
                    }
                }
            }
            for (int i = availableArea.Count - 1; i >= 0; i--) {
                var pos = availableArea[i];
                availableArea.RemoveAt(i);
                if (IsGoodPosForTile(tile, pos)) {
                    return pos;
                }
            }
            return Pos.Invalid;
        }
        private bool IsGoodPosFallback(Tile tile, int p) {
            bool goodStart1 = true;
            bool goodStart2 = true;
            var maxX = tile.Transform.RotatedWidth;
            var maxY = tile.Transform.RotatedHeight;

            for (int x = 0; x < maxX; x++) {
                for (int y = 0; y < maxY; y++) {
                    var pos = Pos.NeighborAt(p, x, y);
                    if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) {
                        goodStart1 = false;
                        break;
                    }
                }
                if (!goodStart1) { break; }
            }

            if (goodStart1) { return true; }

            for (int x = -1; x < maxX - 1; x++) {
                for (int y = -1; y < maxY - 1; y++) {
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

        private static bool BlocksDoorway(Tile tile, int pos) {
            int cnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                var p = Caches.PosIdx[i];
                if (GenUtils.IsDoor(Pos.N(p)) || GenUtils.IsDoor(Pos.S(p))
                        || GenUtils.IsDoor(Pos.E(p)) || GenUtils.IsDoor(Pos.W(p))) {
                    return true;
                }
            }
            return false;
        }

        private bool IsGoodPosForTile(Tile tile, int pos) {
            if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) {
                return false;
            }

            if (isFallback) {
                D.Warn("checking fallback pos");
                if (!IsGoodPosFallback(tile, pos)) { return false; }
                // The relaxed rules still keep doorways open: the passage check below only
                // sees the room's own floor, not the door it walls off
                if (mustAvoidDoors && BlocksDoorway(tile, pos)) { return false; }
                return !mustPreservePassages || KeepsRoomConnected(tile, pos);
            }

            int scnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int i = 0; i < scnt; i++) {
                var p = Caches.PosIdx[i];
                if (!EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                    return false;
                }
            }

            if (mustAvoidDoors) {
                for (int i = 0; i < scnt; i++) {
                    var p = Caches.PosIdx[i];
                    if (GenUtils.IsDoor(p)) { return false; }
                    var nearDoor = false;
                    Pos.ForEachAround(p, 1, true, true, (n) => {
                        if (GenUtils.IsDoor(n)) {
                            nearDoor = true;
                            return false;
                        }
                        return true;
                    });
                    if (nearDoor) { return false; }
                }
            }

            if (mustPreservePassages && !KeepsRoomConnected(tile, pos)) {
                return false;
            }

            int cnt = tile.Transform.OutlineAt(pos, Caches.PosIdx);

            // we will only allow building disks near other disks
            for (int j = 0; j < cnt; j++) {
                var p = Caches.PosIdx[j];
                if (!EntityUtils.IsEmptyFloorAt(p)) {
                    var t = EntityUtils.GetTopmostParentTile(p);
                    // allow rows of same stuff
                    if (t == null || t.Definition.IdH != tile.Definition.IdH) {
                        return false;
                    }
                }
            }
            return true;
        }

        public override void Apply() {
            HashSet<int> usedPositions = new();
            var availableArea = new List<int>();
            ReloadAvailableArea(availableArea);
            int amount = PlaceTiles(wantedAmount, availableArea, usedPositions);
            if (amount < wantedAmount && !isFallback) {
                SwitchToFallback(availableArea);
                amount += PlaceTiles(wantedAmount - amount, availableArea, usedPositions);
            }
            if (amount < wantedAmount && (mustBeIndoors || mustBeOutdoors)) {
                // Every spot the room restriction allows is taken, or would seal a
                // passage. A wrecked ship can have plenty of floor and only a few
                // narrow rooms left, so lift the restriction rather than place
                // nothing: the crew reaches a breached section, an enclosed storage
                // it never does.
                D.Warn("{0}: placed {1}/{2} with the indoor/outdoor restriction, lifting it",
                    LogPrefix, amount, wantedAmount);
                mustBeIndoors = false;
                mustBeOutdoors = false;
                ReloadAvailableArea(availableArea);
                amount += PlaceTiles(wantedAmount - amount, availableArea, usedPositions);
            }
            if (amount < wantedAmount) {
                D.Err("We failed to find a position! Placed {0}/{1} in place tile gen stage: {2}",
                    amount, wantedAmount, defId);
            }
        }

        // Places up to wanted tiles from availableArea, switching to the relaxed
        // fallback rules once the strict ones run dry. Returns how many were placed.
        private int PlaceTiles(int wanted, List<int> availableArea,
                HashSet<int> usedPositions) {
            int amtAttempts = 10 * wanted;
            int amount = 0;
            int lastBuiltPos = Pos.Invalid;
            Tile proto = null;
            int placedInARow = 0;
            while (amount < wanted && amtAttempts-- > 0) {
                int buildAttempts = 100;
                while (buildAttempts-- > 0) {
                    if (proto == null) {
                        proto = CreatePrototype();
                    }
                    var pos = findPosition(proto, rng, spec, lastBuiltPos, placedInARow,
                            availableArea, usedPositions);
                    if (pos == Pos.Invalid) {
                        if (!isFallback) {
                            SwitchToFallback(availableArea);
                            D.Warn("attempts left: {0} * {1}", amtAttempts, buildAttempts);
                            buildAttempts = 100;
                            continue;
                        }
                        // Out of candidates under the current rules
                        return amount;
                    }
                    #if !PRODUCTION_BUILD
                    if (!IsWithinGenArea(pos)) {
                        D.Err("Within gen area should have been checked in find function! {0}", this);
                        continue;
                    }
                    #endif
                    // The being check sits here, not in the find function: only a spot that
                    // would have been built changes, so curated seeds keep their ships
                    if (CmdPlaceTile.CanPlace(pos, proto, proto.Definition.LayerId, warn: false)
                            && !GenUtils.CoversBeing(S, proto, pos)) {
                        var placed = BuildTile(pos, proto, true);
                        proto = null;
                        if (placed == null) {
                            D.Err("Placed tile is null!");
                            placedInARow = 0;
                            continue;
                        }
                        if (placed.Transform.IsMulti) {
                            int subPos = placed.Transform.GetSubtilePos(Caches.PosIdx);
                            for (int i = 0; i < subPos; i++) {
                                availableArea.Remove(Caches.PosIdx[i]);
                                usedPositions.Add(Caches.PosIdx[i]);
                            }
                        } else {
                            availableArea.Remove(pos);
                            usedPositions.Add(pos);
                        }
                        lastBuiltPos = pos;
                        amount++;
                        placedInARow++;
                        if (placedInARow == maxInRow) {
                            lastBuiltPos = Pos.Invalid;
                            placedInARow = 0;
                        }
                        break; // inner loop
                    }
                    placedInARow = 0;
                }
            }
            return amount;
        }

        private void ReloadAvailableArea(List<int> availableArea) {
            var area = S.Sys.Areas;
            availableArea.Clear();
            foreach (var a in area.SolidArea) {
                if (IsWithinGenArea(a)) {
                    if (mustBeOutdoors && !area.IsOutside(a)) { continue; }
                    if (mustBeIndoors && area.IsOutside(a)) { continue; }
                    availableArea.Add(a);
                }
            }
            if (isFallback && Arrays.IsEmpty(availableArea)) {
                foreach (var a in area.SolidArea) {
                    if (IsWithinGenArea(a)) {
                        availableArea.Add(a);
                    }
                }
                if (Arrays.IsEmpty(availableArea)) {
                    D.Err("Available area is null or empty after fallback! {0}",
                        availableArea);
                }
            }
            if (Arrays.IsEmpty(availableArea)) {
                return;
            }
            availableArea.Sort();
            Arrays.Shuffle(rng, availableArea);
        }

        private void SwitchToFallback(List<int> availableArea) {
            if (isFallback) { return; }
            D.Warn("Switching to fallback for {0}", defId);
            isFallback = true;
            ReloadAvailableArea(availableArea);
        }

        private Tile CreatePrototype() {
            var proto = EntityUtils.CreatePrototype<Tile>(defId);
            if (Arrays.Count(rotations) > 2) {
                proto.Transform.Rotation = rng.From(rotations);
            } else if (Arrays.Count(rotations) == 1) {
                proto.Transform.Rotation = rotations[0];
            }

            return proto;
        }
    }
}
