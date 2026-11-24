using System.Collections.Generic;
using System.Linq;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    // Doors on a spanning tree of the room graph instead of on every shared wall, plus a
    // quarter of the remaining walls for some loops. Edges to corridors win the tree first, so
    // corridors become the hubs people walk through instead of a grid of rooms that all open
    // into each other.
    public class TreeDoorGenStage : InnerDoorGenStage {
        private const float ExtraDoorChance = 0.25f;
        private readonly Dictionary<string, (int, int)> pairIds = new();

        public override void Apply() {
            createdDoors = 0;
            edges.Clear();
            pairIds.Clear();
            var areas = S.Sys.Areas.Areas.OrderBy(a => a.Value.Min()).ToArray();
            var corridorAreas = new HashSet<int>();
            foreach (var area in areas) {
                foreach (var p in area.Value) {
                    if (spec.CorridorTiles.Contains(p)) {
                        corridorAreas.Add(area.Key);
                        break;
                    }
                }
                GatherEdges(area.Key, area.Value);
            }

            var candidates = edges.Keys.OrderBy(k => k).Select(k => {
                var (a, b) = pairIds[k];
                var hub = corridorAreas.Contains(a) || corridorAreas.Contains(b);
                return (Key: k, A: a, B: b, Weight: (hub ? 0f : 1f) + rng.Value);
            }).OrderBy(e => e.Weight).ToList();

            // Kruskal
            var parent = new Dictionary<int, int>();
            int Find(int x) {
                if (!parent.TryGetValue(x, out var p)) {
                    parent[x] = x;
                    return x;
                }
                if (p == x) { return x; }
                var root = Find(p);
                parent[x] = root;
                return root;
            }
            // An edge joins the tree only once its door is built, so a wall with no room for
            // a door cannot leave a room cut off
            foreach (var e in candidates) {
                var ra = Find(e.A);
                var rb = Find(e.B);
                if (ra == rb && !rng.Chance(ExtraDoorChance)) { continue; }
                if (!BuildDoorBetween(edges[e.Key], e.A, e.B)) { continue; }
                createdDoors++;
                if (ra != rb) {
                    parent[ra] = rb;
                }
            }
        }

        // A wall tile with one room on each side, in line with its neighbors, away from other
        // doors; nearest the middle of the shared wall
        private bool BuildDoorBetween(HashSet<int> wall, int a, int b) {
            var areas = S.Sys.Areas;
            var good = new List<int>();
            foreach (var p in wall) {
                if (!EntityUtils.IsWallAt(p) || EntityUtils.WallAt(p).Door != null) { continue; }
                var n = areas.At(Pos.N(p));
                var s = areas.At(Pos.S(p));
                var e = areas.At(Pos.E(p));
                var w = areas.At(Pos.W(p));
                var ns = (n == a && s == b) || (n == b && s == a);
                var ew = (e == a && w == b) || (e == b && w == a);
                if (ns && !(EntityUtils.IsWallAt(Pos.E(p)) && EntityUtils.IsWallAt(Pos.W(p)))) { continue; }
                if (ew && !(EntityUtils.IsWallAt(Pos.N(p)) && EntityUtils.IsWallAt(Pos.S(p)))) { continue; }
                if (!ns && !ew) { continue; }
                if (IsDoorNear(p)) { continue; }
                good.Add(p);
            }
            if (good.Count == 0) { return false; }
            good.Sort();
            // middle third of the wall, picked at random
            var from = good.Count / 3;
            var to = Mathf.Max(from + 1, good.Count - good.Count / 3);
            var pick = good[rng.Range(from, to)];
            if (BuildDoorAt(pick)) { return true; }
            // A wall the door could not replace must not count as a way through, or the
            // rooms behind it stay cut off
            foreach (var p in good) {
                if (p != pick && BuildDoorAt(p)) { return true; }
            }
            return false;
        }

        private static bool IsDoorNear(int p) {
            return EntityUtils.WallAt(Pos.N(p))?.Door != null
                || EntityUtils.WallAt(Pos.S(p))?.Door != null
                || EntityUtils.WallAt(Pos.E(p))?.Door != null
                || EntityUtils.WallAt(Pos.W(p))?.Door != null;
        }

        private void GatherEdges(int areaId, HashSet<int> area) {
            if (!IsWithinGenArea(area.First())) { return; }
            // the center room gets its doors from MissingDoorGenStage
            if (GenUtils.IsCenterRoom(spec, area)) { return; }
            var outline = new HashSet<int>();
            S.Sys.Areas.OutlineOfArea(areaId, outline);
            var sortedOutline = outline.ToList();
            sortedOutline.Sort();
            foreach (var wallTile in sortedOutline) {
                Pos.ForEachAround(wallTile, 1, false, false, neighbor => {
                    if (area.Contains(neighbor)) { return true; }
                    if (outline.Contains(neighbor)) { return true; }
                    var otherId = S.Sys.Areas.At(neighbor);
                    // rooms only: walls (-1) and the floor outside (below -1) would join the
                    // tree as nodes, and two rooms with a door into the same one would count
                    // as connected. AirlockGenStage makes the ways out.
                    if (otherId <= 0) { return true; }
                    var lo = areaId < otherId ? areaId : otherId;
                    var hi = areaId < otherId ? otherId : areaId;
                    var key = $"{lo}:{hi}";
                    pairIds[key] = (lo, hi);
                    var edge = edges.GetInit(key);
                    edge.Add(wallTile);
                    return true;
                });
            }
        }
    }
}
