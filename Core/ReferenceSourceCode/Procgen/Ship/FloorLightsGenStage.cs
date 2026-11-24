using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Priority_Queue;
using KL.Collections;
using System.Linq;
using Game.Constants;
using UnityEngine;
using KL.Utils;
using Game.Components;
using KL.Randomness;

namespace Game.Procgen {
    public class FloorLightsGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private readonly Dictionary<int, List<int>> roomLights = new();
        private new Rng rng;
        public override void Apply() {
            // get all rooms with their sizes
            roomLights.Clear();
            rng = spec.CosmeticRng.Fork();

            foreach (var area in S.Sys.Areas.Areas) {
                if (!IsWithinGenArea(area.Value.First())) { continue; }
                AddFloorLights(area.Key, area.Value);
            }

            foreach (var area in S.Sys.Areas.OuterAreas) {
                if (!IsWithinGenArea(area.Value.First())) { continue; }
                AddFloorLights(area.Key, area.Value);
            }

            foreach (var roomLight in roomLights) {
                var isDisco = rng.Chance(0.25f);
                float satFrom;
                float satTo;
                float lumFrom;
                float lumTo;
                if (isDisco) {
                    satFrom = 70f;
                    satTo = 95f;
                    lumFrom = 40f;
                    lumTo = 60f;
                } else {
                    satFrom = 10f;
                    satTo = 80f;
                    lumFrom = 60f;
                    lumTo = 90f;
                }

                var color = rng.Color(0, 360, satFrom, satTo, lumFrom, lumTo);
                color.a = 1f;
                for (int i = 0; i < roomLight.Value.Count; i++) {
                    int lightPos = roomLight.Value[i];
                    var floor = EntityUtils.FloorAt(lightPos);
                    if (floor.TryGetComponent<LightSourceComp>(out var l)) {
                        l.SetColor(color);
                    } else {
                        D.Err("No floor light while expected at {0}",
                            Pos.String(lightPos));
                    }
                }
            }
        }

        private void AddFloorLights(int areaId, HashSet<int> area) {
            if (area.Count < 3) { return; }
            // 1. skin the area near the wall
            var reducedArea = PosRect.RemoveOutline(area);

            if (reducedArea.Count == 0) {
                // we have a thin room
                BuildLightsInArea(areaId, area);
            } else {
                BuildLightsInArea(areaId, reducedArea);
            }
        }

        private void BuildLightsInArea(int areaId, HashSet<int> area) {
            if (area.Count < 15) {
                // add light in the center
                var rekt = Pos.Encapsulate(area);
                if (BuildFloorLight(rekt.CenterPos)) {
                    return;
                }
            }

            HashSet<int> originalArea = null;
            // the area is still huge, let's reduce it further
            if (area.Count > 100) {
                originalArea = area;
                area = PosRect.RemoveOutline(area);
                if (Arrays.IsEmpty(area)) { return; }
            }

            var lightsHere = new HashSet<int>();

            int tl = area.First();
            int bl = tl;
            int tr = tl;
            int br = tl;
            foreach (var p in area) {
                Pos.ToXY(p, out var x, out var y);
                Pos.ToXY(tl, out var tlX, out var tlY);
                Pos.ToXY(bl, out var blX, out var blY);
                Pos.ToXY(tr, out var trX, out var trY);
                Pos.ToXY(br, out var brX, out var brY);
                if (x < blX && y < blY) { bl = p; }
                if (x < tlX && y > tlY) { tl = p; }
                if (x > trX && y > trY) { tr = p; }
                if (x > brX && y < brY) { br = p; }
            }

            var queue = new List<int>();
            if (rng.Chance(0.5f)) {
                queue.Add(bl);
                queue.Add(tr);
                queue.Add(tl);
                queue.Add(br);
            } else {
                queue.Add(br);
                queue.Add(tl);
                queue.Add(tr);
                queue.Add(bl);
            }

            queue.AddRange(area);

            BuildLightsIn(lightsHere, queue);

            if (originalArea != null) {
                // see if we left any larger corners unhandled
                BuildLightsIn(lightsHere, originalArea);
            }
            if (!roomLights.TryGetValue(areaId, out var list)) {
                list = new();
                roomLights[areaId] = list;
            }
            list.AddRange(lightsHere);
        }

        private void BuildLightsIn(HashSet<int> lightsHere, ICollection<int> queue) {
            foreach (var p in queue) {
                if (!EntityUtils.IsEmptyFloorAt(p)) {
                    continue;
                }
                bool isAnotherLightTooClose = false;
                Pos.ForEachInRadius(p, 5, false, (subP) => {
                    if (lightsHere.Contains(subP)) {
                        isAnotherLightTooClose = true;
                        return false;
                    }
                    return true;
                });
                if (isAnotherLightTooClose) { continue; }
                if (BuildFloorLight(p)) {
                    lightsHere.Add(p);
                }
            }
        }

        private bool BuildFloorLight(int p) {
            var nowTile = EntityUtils.FloorAt(p);
            if (nowTile == null || nowTile.Integrity == null
                    || !nowTile.Integrity.CanHaveFullIntegrity) {
                // angle floor tile!
                return false;
            }
            if (nowTile.HasComponent<LightSourceComp>()) {
                // D.Warn("Duplicate floor light ordered at {0}", Pos.String(p));
                return false;
            }
            var defId = nowTile.Definition.Id + "Lit";
            var def = The.Defs.TryGet(defId);
            if (def == null) {
                D.Err("Cannot build light with def id: {0}", defId);
                return false;
            }
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            BuildTile(p, proto);
            return true;
        }
    }
}
