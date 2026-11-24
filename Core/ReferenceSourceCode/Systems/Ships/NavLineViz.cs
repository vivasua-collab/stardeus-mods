using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using UnityEngine;
using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class NavLineViz {
        private UILine linePrefab;
        private readonly RectTransform container;
        private UILine line;
        private readonly Color color;
        private Vector2 lastDestLoc;
        private Vector2 lastShipLoc;
        private const float thickness = 16f;

        public bool HasLine => line != null;

        public NavLineViz(RectTransform container, Color color) {
            this.color = color;
            this.container = container;
            linePrefab = ResourceLibrary.FindPrefab<UILine>(UIPrefabId.UILineShipTarget);
        }

        public void Draw(Vector2 shipLoc, Vector2 destLoc, int siblingIndex, float scale) {
            if (line == null) {
                SpawnLine(shipLoc, destLoc, siblingIndex);
            } else if (Vectors.SqrDistance(lastShipLoc, shipLoc) > 0.01f ||
                    Vectors.SqrDistance(lastDestLoc, destLoc) > 0.01f) {
                UpdateLinePoints(shipLoc, destLoc);
            }
            UpdateLine(scale, Vectors.Distance(shipLoc, destLoc));
            lastShipLoc = shipLoc;
            lastDestLoc = destLoc;
        }

        private void SpawnLine(Vector2 shipLoc, Vector2 destLoc, int siblingIndex) {
            var points = BuildSegmentedPoints(shipLoc, destLoc);
            line = PrefabPool.Spawn(linePrefab, Consts.V3Zero, Consts.QId, container);
            line.name = "NavLine";
            line.Load(points, thickness, color, color);
            line.transform.position = Consts.V3Zero;
            line.transform.localPosition = Consts.V3Zero;

            line.transform.SetSiblingIndex(siblingIndex);
        }

        private void UpdateLinePoints(Vector2 shipLoc, Vector2 destLoc) {
            var points = BuildSegmentedPoints(shipLoc, destLoc);
            line.Load(points, thickness, color, color);
        }

        private Vector2[] BuildSegmentedPoints(Vector2 from, Vector2 to) {
            return new Vector2[] { from, to };
        }

        public void UpdateLine(float uiScale, float dist) {
            if (line == null) { return; }
            var adjustedThickness = thickness / uiScale;
            // if (adjustedThickness > 512) {
            //     adjustedThickness = 512f;
            // }
            var wanted = Mathf.RoundToInt(dist / 1000f * uiScale * 10f);
            // var res = Mathf.Lerp(64f, 8f, adjustedThickness / 256f);
            line.SetThickness(adjustedThickness);
            line.SetResolution(wanted);
        }

        public void Clear() {
            if (line != null) {
                PrefabPool.Despawn(line.gameObject);
                line = null;
            }
            lastDestLoc = Vector2.zero;
            lastShipLoc = Vector2.zero;
        }
    }
}
