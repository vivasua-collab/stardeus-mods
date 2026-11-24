using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Priority_Queue;
using KL.Collections;
using System.Linq;
using Game.Constants;
using UnityEngine;
using KL.Utils;
using System.Collections;
using Game.CodeGen;
using Game.Components;
using Game.Utils;

namespace Game.Procgen {
    public class RangedDeviceGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        private Dictionary<int, HashSet<int>> allAreas;
        private int range;
        private readonly Def def;
        private readonly bool indoors;
        private readonly int minAreaSize;
        public RangedDeviceGenStage(string def, int range, bool indoors, int minAreaSize) {
            this.def = The.Defs.Get(def);
            this.indoors = indoors;
            this.range = range;
            this.minAreaSize = minAreaSize;
        }

        public override IEnumerator ApplyAsync() {
            // get all rooms with their sizes
            allAreas = indoors ? S.Sys.Areas.Areas : S.Sys.Areas.OuterAreas;

            // sort rooms by size, biggest to smallest
            var sortedAreas = new SimplePriorityQueue<int>(128);
            foreach (var area in allAreas) {
                var roomId = area.Key;
                var roomSize = area.Value.Count;
                sortedAreas.Enqueue(roomId, -roomSize);
            }

            // Fill the area up
            while (sortedAreas.Count > 0) {
                var nextAreaId = sortedAreas.Dequeue();
                // yield return Viz(S.Sys.Areas.AreaById(nextAreaId).First(), 1f);
                EnsureCoverage(nextAreaId);
                if (FrameTimer.HasToYieldSlow()) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
            }
        }

        private void RemoveCoveredPositions(List<int> roomTiles, int pos) {
            int tilesBefore = roomTiles.Count;
            var roomPos = pos;
            Pos.ForEachInRadius(pos, range, true, (p) => {
                roomTiles.Remove(p);
                return true;
            });
            int tilesAfter = roomTiles.Count;
            // D.Warn("Removed radius {0} at pos {1}. Tile count {2} -> {3}",
            //     range, Pos.String(pos), tilesBefore, tilesAfter);
        }

        private void EnsureCoverage(int areaId) {
            // Don't build connectors in tiny rooms
            var area = allAreas[areaId];
            if (area.Count < minAreaSize) { return; }
            D.Warn("Ensuring coverage in area: {0}", Pos.String(area.First()));
            var numWantedDevices = Mathf.CeilToInt(area.Count /
                rng.Range(80f, 120f));
            var devicesInRoom = new List<int>();
            foreach (var p in area) {
                var ob = EntityUtils.ObjAt(p);
                if (ob != null && ob.ENode != null) {
                    devicesInRoom.Add(p);
                }
            }
            numWantedDevices = Mathf.Max(numWantedDevices,
                Mathf.CeilToInt(devicesInRoom.Count / 6));
            var needToBuild = numWantedDevices; // - existingConnectorCount;
            var tiles = new List<int>(allAreas[areaId]);
            tiles = tiles.Except(devicesInRoom).ToList();
            tiles.Sort();

            bool triedCenter = false;
            int attempts = 300;

            while (needToBuild > 0 && tiles.Count > 5 && attempts-- > 0)  {
                if (!triedCenter) {
                    triedCenter = true;
                    var rect = Pos.Encapsulate(tiles);
                    var p = rect.CenterPos;
                    if (!tiles.Contains(p)) { continue; }
                    if (!IsGoodObjectLocation(p)) {
                        continue;
                    }
                    if (TryBuildNewDevice(p)) {
                        RemoveCoveredPositions(tiles, p);
                        needToBuild--;
                    } else {
                    }
                    continue;
                }
                // We already built a center connector if we could
                var randomPos = rng.From(tiles);
                // We cannot afford trying to find a good location anymore
                if (attempts > 5 && !IsGoodObjectLocation(randomPos)) {
                    continue;
                }
                if (TryBuildNewDevice(randomPos)) {
                    RemoveCoveredPositions(tiles, randomPos);
                    needToBuild--;
                } else {
                }
            }
            if (needToBuild > 0) {
                D.Warn("GEN_SHIP {0} Failed to build {1}/{2} {4} in room {3}",
                   LogPrefix, needToBuild, numWantedDevices, areaId, def);
            }
        }

        private bool TryBuildNewDevice(int p) {
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            proto.Transform.Rotation = rng.From(Facing.Types);
            BuildTile(p, proto);
            var con = EntityUtils.GetTopmostTile(p);
            return con.Definition == def;
        }
    }
}
