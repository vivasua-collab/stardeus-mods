#if UNITY_EDITOR
//#define DEBUG_GEN_ROOMS
#endif
using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Priority_Queue;
using KL.Collections;
using KL.Utils;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using System;
using Game.CodeGen;

namespace Game.Procgen {
    public class ShipRoomObjectsGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private Dictionary<int, HashSet<int>> allAreas;
        private Dictionary<RoomConfig, int> roomBudgets;
        private List<RoomConfig> roomConfigs;
        private List<RoomConfig> allRoomConfigs;
        public override void Apply() {
            roomBudgets = new();
            // get all rooms with their sizes
            allAreas = S.Sys.Areas.Areas;
            // get all room config definitions
            allRoomConfigs = The.ModLoader.LoadObjects<RoomConfig>(
                "Config/Generation/Rooms");
            roomConfigs = allRoomConfigs
                    .Where(r => rng.Chance(r.Chance))
                    .OrderBy(r => r.Type)
                    .OrderBy(r => rng.Value)
                    .ToList();
            foreach (var roomCfg in roomConfigs) {
                roomBudgets[roomCfg] = rng.Range(roomCfg.CountMin, roomCfg.CountMax);
            }
            // sort rooms by size, biggest to smallest
            var sortedAreas = new SimplePriorityQueue<int>(128);
            foreach (var area in allAreas) {
                var roomSize = area.Value.Count;
                var roomId = area.Key;
                sortedAreas.Enqueue(roomId, -roomSize);
            }

            // Fill the area up
            while (sortedAreas.Count > 0) {
                var nextAreaId = sortedAreas.Dequeue();
                FillArea(nextAreaId);
            }
        }

        private void FillArea(int areaId) {
            #if DEBUG_GEN_ROOMS
            // D.Warn("GEN_SHIP {0} Filling area {1}", LogPrefix, areaId);
            #endif
            var tiles = new List<int>(allAreas[areaId]);
            tiles.Sort();
            var roomRect = Pos.Encapsulate(tiles);
            var size = tiles.Count;
            if (size < 5) { return; }
            RoomConfig bestCfg = null;
            if (IsRoomOddlyShaped(tiles, roomRect)) {
                bestCfg = allRoomConfigs.FirstOrDefault(
                    rc => rc.Type.StartsWith("Corridor"));
                #if DEBUG_GEN_ROOMS
                // D.Warn("GEN_SHIP {0} Will build a corridor! {1}", LogPrefix, bestCfg);
                #endif
            } else {
                // Find the biggest room that we can fit in this area
                var sortedConfigs = roomConfigs.OrderBy(r => rng.Value).ToList();
                foreach (var cfg in sortedConfigs) {
                    if (size > cfg.SizeMax) { continue; }
                    if (size < cfg.SizeMin) { continue; }
                    // Size is fine
                    // We already filled out this room type
                    if (roomBudgets.Get(cfg, 0) < 1) { continue; }
                    // We had no config at all
                    if (bestCfg == null) {
                        bestCfg = cfg;
                        continue;
                    }
                    if (cfg.SizeMax > bestCfg.SizeMax) {
                        bestCfg = cfg;
                        continue;
                    }
                }
            }
            if (bestCfg == null) {
                #if DEBUG_GEN_ROOMS
                D.Warn("GEN_SHIP {0} Could not find a suitable config for room of size: {1}",
                    LogPrefix, size);
                #endif
                // We don't have anything to place in the room
                // TODO some fallback
                return;
            } else {
                BuildObjects(tiles, bestCfg);
            }
        }

        private void BuildObjects(List<int> tiles, RoomConfig cfg) {
            roomBudgets.GetIncrement(cfg, -1);
            var roomSize = tiles.Count;
            var availableTiles = new List<int>(tiles);
            for (int i = availableTiles.Count - 1; i >= 0; i--) {
                var idx = availableTiles[i];
                if (IsBlockingADoor(idx)) {
                    availableTiles.RemoveAt(i);
                }
            }

            var sizeBudget = availableTiles.Count;
            var sortedTemplates = cfg.CompatibleTemplates
                .OrderBy(t => rng.Value).ToList();
            var fillRate = 0f;
            foreach (var tplId in sortedTemplates) {
                var tpl = The.Defs.TryGet(tplId);
                if (tpl == null) {
                    D.Err("RoomConfig {0} has invalid template {1}",
                        cfg.Type, tplId);
                    continue;
                }
                // Let's check if template can fit
                var transform = tpl.ComponentConfigFor(ComponentIdH.TileTransform);
                var tplWidth = transform.GetInt(PropertyIdH.Width, 1);
                var tplHeight = transform.GetInt(PropertyIdH.Height, 1);
                var tplSize = tplWidth * tplHeight;
                if (tplSize > sizeBudget) {
                    // This template would not fit
                    continue;
                }

                var tplProto = EntityUtils.CreatePrototype<Tile>(tplId);

                if (TryBuildTemplateIn(availableTiles, tplProto)) {
                    // TODO template counter
                    sizeBudget -= tplSize;
                    fillRate = (roomSize - sizeBudget) / (float) roomSize;
                    // Room already full
                    if (fillRate > cfg.FillMax) { return; }
                }
            }

            if (fillRate < cfg.FillMin && !Arrays.IsEmpty(cfg.CompatibleObjects)) {
                // Let's generate a bunch of extra objects

                var sortedObjects = cfg.CompatibleObjects
                    .OrderBy(t => rng.Value).ToList();
                foreach (var obj in sortedObjects) {
                    try {
                        var proto = EntityUtils.CreatePrototype<Tile>(obj);
                        if (proto == null) { continue; }
                        var size = proto.Transform.Width * proto.Transform.Height;
                        if (size > sizeBudget) { return; }
                        var attempts = rng.Range(1, 10);
                        while (attempts-- > 0) {
                            if (TryBuildObjectIn(availableTiles, proto)) {
                                sizeBudget -= size;
                                fillRate = (roomSize - sizeBudget) / (float) roomSize;
                                // Room already full
                                if (fillRate > cfg.FillMax) { return; }
                                break;
                            }
                        }
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed creating gen object: {0}", obj);
                    }
                }
            }

            if (!Arrays.IsEmpty(cfg.RequiredObjects)) {
                var existingObjects = new HashSet<string>();
                foreach (var tile in tiles) {
                    var obj = EntityUtils.ObjAt(tile);
                    if (obj != null && !obj.Transform.IsChild) {
                        existingObjects.Add(obj.Definition.Id);
                    }
                }
                // Ensure required objects are there
                foreach (var obj in cfg.RequiredObjects) {
                    if (!existingObjects.Contains(obj)) {
                        BuildRequiredObject(availableTiles, obj);
                    }
                }
            }
        }

        private void BuildRequiredObject(List<int> tiles, string id) {
            var availableTiles = new List<int>(tiles).OrderBy(t => rng.Value);
            #if DEBUG_GEN_ROOMS
            // D.Warn("GEN_SHIP {0} building required tile {1}", LogPrefix, id);
            #endif
            var proto = EntityUtils.CreatePrototype<Tile>(id);
            if (proto == null) { return; }
            foreach (var idx in availableTiles) {
                if (CmdPlaceTile.CanPlace(idx, proto, WorldLayer.Objects, warn: false)) {
                    BuildTile(idx, proto);
                    return;
                }
            }
        }

        private bool IsBlockingADoor(int idx) {
            var isBlocking = false;
            Pos.ForEachAround(idx, 1, false, false, (nIdx) => {
                if (GenUtils.IsDoor(idx)) {
                    isBlocking = true;
                    return false;
                }
                return true;
            });
            return isBlocking;
        }

        private bool TryBuildTemplateIn(List<int> tiles, Tile tpl) {
            // Let's pick a random corner
            var corners = new List<int>() { 0, 1, 2, 3 };
            corners = corners.OrderBy((i) => rng.Value).ToList();

            if (tpl.GetComponent<TemplateComp>().PlaceInTheMiddle) {
                if (TryBuildTemplateInMiddle(tiles, tpl)) { return true; }
            }

            for (int i = 0; i < 4; i++) {
                var corner = corners[i];
                if (TryBuildTemplateInCorner(tiles, tpl, corner)) {
                    return true;
                }
            }

            return TryBuildTemplateInMiddle(tiles, tpl);
        }

        private bool TryBuildTemplateInMiddle(List<int> tiles, Tile tpl) {
            var areaRect = Pos.Encapsulate(new HashSet<int>(tiles));
            var startPos = areaRect.CenterPos + EntityUtils.ToPos(
                rng.InsideUnitCircle() * areaRect.MinEdgeLength * 0.2f);
            return TryPlacingTemplateAt(tiles, startPos, tpl);
        }

        private bool TryBuildTemplateInCorner(List<int> tiles, Tile tpl,
                int corner) {
            var areaRect = Pos.Encapsulate(new HashSet<int>(tiles));
            var transform = tpl.Transform;
            var tplWidth = transform.Width;
            var tplHeight = transform.Height;
            int startPos = Pos.Invalid;
            switch (corner) {
                case 0: {
                    var bl = areaRect.BottomLeft;
                    startPos = Pos.FromXY(bl.X, bl.Y);
                    break;
                }
                case 1: {
                    var tl = areaRect.TopLeft;
                    startPos = Pos.FromXY(tl.X, tl.Y - tplHeight + 1);
                    break;
                }
                case 2: {
                    var tr = areaRect.TopRight;
                    startPos = Pos.FromXY(tr.X - tplWidth + 1, tr.Y - tplHeight + 1);
                    break;
                }
                case 3: {
                    var br = areaRect.BottomRight;
                    startPos = Pos.FromXY(br.X - tplWidth + 1, br.Y);
                    break;
                }
            }
            return TryPlacingTemplateAt(tiles, startPos, tpl);
        }

        private bool TryPlacingTemplateAt(List<int> tiles, int startPos, Tile tpl) {
            if (!tiles.Contains(startPos)) {
                return false;
            }
            if (!CmdPlaceTile.CanPlace(startPos, tpl, tpl.Definition.LayerId, warn: false)) {
                return false;
                }
            BuildTile(startPos, tpl);
            var tplRect = new PosRect(startPos,
                Pos.NeighborAt(startPos,
                    tpl.Transform.Width, tpl.Transform.Height));
            tplRect.ForEach((posIdx) => tiles.Remove(posIdx));
            return true;
        }

        private bool TryBuildObjectIn(List<int> area, Tile proto) {
            int objectPoint = 0;
            var startPos = rng.From(area);
            var distance = rng.Range(2, 10);
            var tr = proto.Transform;
            tr.Rotation = tr.RandomValidRotation(rng);
            Pos.ForEachAroundRandom(rng, startPos, distance, false, true, (n) => {
                if (!area.Contains(n)) { return true; }
                if (IsGoodObjectLocation(n)) {
                    objectPoint = n;
                    return false;
                }
                return true;
            });

            if (objectPoint == 0) { return false; }

            BuildTile(objectPoint, proto);
            return EntityUtils.IsObjAt(objectPoint);
        }
    }
}
