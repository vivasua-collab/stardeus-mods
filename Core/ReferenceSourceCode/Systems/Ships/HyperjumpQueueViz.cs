using System.Collections.Generic;
using Game.Constants;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class HyperjumpQueueViz {
        private UILine linePrefab;
        private readonly RectTransform container;
        private UILine line;
        private UILine linePassive;
        private readonly Color colorActive;
        private readonly Color colorPassive;
        private readonly float thickness;
        private readonly string name;

        public HyperjumpQueueViz(string name, RectTransform container, float thickness, Color colActive, Color colPassive) {
            colorActive = colActive;
            colorPassive = colPassive;
            this.name = name;
            this.thickness = thickness;
            linePrefab = ResourceLibrary.FindPrefab<UILine>(UIPrefabId.UILineHyperjumpQueue);
            this.container = container;
        }

        public void Draw(int numActiveNodes,
                ISpaceEntity start, LinkedList<ISpaceEntity> q, string layer) {
            Clear();
            if (q.Count == 0) { return; }
            if (start.SO.UI == null) { return; }
            var mul = 20f;
            if (layer == UniverseLayer.Universe) {
                mul = 10f;
            }

            var next = q.First;
            int attempts = 100;

            // Collect all points
            var allPoints = new Vector2[q.Count + 1];
            int i = 0;
            allPoints[i] = start.UIPosition;

            while (next != null && attempts-- > 0) {
                i++;
                var cur = next.Value;
                next = next.Next;
                if (cur.SO.UI == null) {
                    // Node isn't instantiated in the currently displayed container (e.g.
                    // the view was switched mid-queue-change) - bail rather than draw a
                    // segment to the map origin. Nothing has been spawned yet at this
                    // point, so Clear() at the top of this method already covers cleanup.
                    return;
                }
                allPoints[i] = cur.UIPosition;
            }

            int totalPoints = i + 1;
            int activePointCount = Mathf.Min(numActiveNodes + 1, totalPoints);

            // Build active line if there are active nodes
            if (activePointCount > 1) {
                var activePoints = ExtractPoints(allPoints, 0, activePointCount, mul);
                line = SpawnLine(activePoints, "HyperQueueActive", colorActive);
            }

            // Build passive line if there are remaining nodes after active portion
            int passiveStartIndex = activePointCount - 1;
            int passivePointCount = totalPoints - passiveStartIndex;
            if (passivePointCount > 1) {
                var passivePoints = ExtractPoints(allPoints, passiveStartIndex, passivePointCount, mul);
                linePassive = SpawnLine(passivePoints, "HyperQueuePassive", colorPassive);
            }
        }

        private Vector2[] ExtractPoints(Vector2[] source, int startIndex, int count, float spreadMul) {
            var segmentLength = Tunables.StarmapSpaceObjectSpread * spreadMul;
            var result = new List<Vector2>();

            for (int j = 0; j < count; j++) {
                var current = source[startIndex + j];
                if (j == 0) {
                    result.Add(current);
                    continue;
                }

                var previous = source[startIndex + j - 1];
                var dist = Vector2.Distance(previous, current);
                int numSegments = Mathf.Max(1, Mathf.CeilToInt(dist / segmentLength));

                for (int s = 1; s <= numSegments; s++) {
                    float t = (float)s / numSegments;
                    result.Add(Vector2.Lerp(previous, current, t));
                }
            }

            return result.ToArray();
        }

        private UILine SpawnLine(Vector2[] points, string name, Color color) {
            var spawned = PrefabPool.Spawn(linePrefab, Consts.V3Zero, Consts.QId, container);
            spawned.name = $"{name}_{this.name}";
            spawned.Load(points, thickness, color, color);
            spawned.transform.SetAsFirstSibling();
            spawned.transform.position = Consts.V3Zero;
            spawned.transform.localPosition = Consts.V3Zero;
            return spawned;
        }

        public void UpdateLine(float uiScale) {
            var adjustedThickness = thickness / uiScale;
            line?.SetThickness(adjustedThickness);
            linePassive?.SetThickness(adjustedThickness);
        }

        private void DespawnLine(ref UILine lineRef) {
            if (lineRef != null) {
                PrefabPool.Despawn(lineRef.gameObject);
                lineRef = null;
            }
        }

        public void Clear() {
            DespawnLine(ref line);
            DespawnLine(ref linePassive);
        }
    }
}
