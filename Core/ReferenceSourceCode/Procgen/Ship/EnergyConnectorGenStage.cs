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
using Game.Utils;

namespace Game.Procgen {
    public class EnergyConnectorGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        private Dictionary<int, HashSet<int>> allAreas;

        public override IEnumerator ApplyAsync() {
            // get all rooms with their sizes
            allAreas = S.Sys.Areas.Areas;

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
                EnsureConnector(nextAreaId);
                if (FrameTimer.HasToYieldSlow()) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
            }
        }

        private void RemoveCoveredPositions(List<int> roomTiles, int conPos, int range) {
            var roomPos = conPos;
            Pos.ForEachInRadius(conPos, range, true, (p) => {
                roomTiles.Remove(p);
                return true;
            });
        }

        private void EnsureConnector(int areaId) {
            // Don't build connectors in tiny rooms
            var area = allAreas[areaId];
            if (area.Count < 10) { return; }
            var numWantedConnectors = Mathf.CeilToInt(area.Count /
                rng.Range(80f, 120f));
            var energyNodesInRoom = new List<int>();
            foreach (var p in area) {
                var ob = EntityUtils.ObjAt(p);
                if (ob != null && ob.ENode != null) {
                    energyNodesInRoom.Add(p);
                }
            }
            numWantedConnectors = Mathf.Max(numWantedConnectors,
                Mathf.CeilToInt(energyNodesInRoom.Count / 6));
            var needToBuild = numWantedConnectors; // - existingConnectorCount;
            var tiles = new List<int>(allAreas[areaId]);
            tiles = tiles.Except(energyNodesInRoom).ToList();
            tiles.Sort();

            bool triedCenter = false;
            int attempts = 300;

            while (needToBuild > 0 && tiles.Count > 5 && attempts-- > 0)  {
                if (!triedCenter) {
                    triedCenter = true;
                    var rect = Pos.Encapsulate(tiles);
                    var p = rect.CenterPos;

                    if (!IsGoodObjectLocation(p)) { continue; }
                    if (TryBuildNewConnector(p, true)) {
                        RemoveCoveredPositions(tiles, p, 7);
                        needToBuild--;
                    }
                    continue;
                }
                // We already built a center connector if we could
                var randomPos = rng.From(tiles);
                // We cannot afford trying to find a good location anymore
                if (attempts > 5 && !IsGoodObjectLocation(randomPos)) {
                    continue;
                }
                if (TryBuildNewConnector(randomPos, rng.Chance(0.2f))) {
                    RemoveCoveredPositions(tiles, randomPos, 10);
                    needToBuild--;
                }
            }
            if (needToBuild > 0) {
                // D.Warn("GEN_SHIP {0} Failed to build {1}/{2} connectors in room {3}",
                //    LogPrefix, needToBuild, numWantedConnectors, areaId);
            }
        }

        private bool TryBuildNewConnector(int p, bool fat) {
            var defId = fat ? DefIds.ObjectsDevicesEnergyConnectorBig
                : DefIds.ObjectsDevicesEnergyConnector;
            var proto = EntityUtils.CreatePrototype<Tile>(defId);
            BuildTile(p, proto);
            var con = EntityUtils.ObjAt(p);
            if (con == null) {
                return false;
            }
            return true;
        }
    }
}
