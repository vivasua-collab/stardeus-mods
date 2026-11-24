using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Game.Constants;
using Game.Data;
using KL.Algos;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class IslandCreationGenStage : PreviewBaseShipGenStage {
        private List<int> tiles = new();
        public override bool ProducesIslands => true;
        public override void Apply() {
            tiles.Clear();
            // Less fill, higher chance of addons
            var chance = Mathf.Clamp01(GeometryFillRatio());
            // Rear wings
            if (rng.Chance(chance)) {
                var x = rng.Range(0, spec.TemplateWidth / 4);
                var y = rng.Range(spec.TemplateHeight - spec.TemplateHeight / 4,
                    spec.TemplateHeight);
                var w = rng.Range(1, 5);
                var h = rng.Range(1, 4);
                for (int i = x; i < x + w; i++) {
                    for (int j = y - h; j < y + 1; j++) {
                        // skip top-right corner
                        if (i == x + w - 1 && j == y) { continue; }
                        // skip bottom-left corner
                        if (i == x && j == y - h) { continue; }
                        Set(i, j);
                    }
                }
            }
            // Front nose
            if (rng.Chance(chance)) {
                var x = spec.TemplateWidth - 1;
                var y = rng.Range(0, spec.TemplateHeight / 5);
                Set(x, y);
            }

            // Mid flaps
            if (rng.Chance(chance)) {
                var x = rng.Range(spec.TemplateWidth / 4, spec.TemplateWidth / 2);
                var y = rng.Range(spec.TemplateHeight - 2, spec.TemplateHeight);
                var w = rng.Range(1, 5);
                var h = rng.Range(1, 3);
                for (int i = x; i < x + w; i++) {
                    for (int j = y - h; j < y + 1; j++) {
                        // skip top-right corner
                        if (i == x + w - 1 && j == y) { continue; }
                        // skip bottom-left corner
                        if (i == x && j == y - h) { continue; }
                        Set(i, j);
                    }
                }
            }
            BuildTiles(tiles, DefIds.StructureFloorsFloor02);
        }

        // gen_ship 789828102 253 80 - bad ship
        private float GeometryFillRatio() {
            var tiles = new List<int>();
            spec.ForEachTile((idx) => {
                if (isPreview) {
                    Pos.ToXY(idx, out var tx, out var ty);
                    if (previewTex.GetPixel(tx, ty) != Consts.ColorBlack) {
                        tiles.Add(idx);
                    }

                } else {
                    if (EntityUtils.IsFloorAt(idx)) {
                        tiles.Add(idx);
                    }
                }
            });
            var tilesRect = Pos.Encapsulate(tiles);
            return tiles.Count / (float) tilesRect.Size;
        }

        private void Set(int x, int y, CellType type = CellType.Skin) {
            if (spec.IsEmpty(x, y, false, true, false)) {
                spec.SetCell(x, y, type);
                AppendAdjustedPositionMirrored(x, y, tiles);
            }
        }
    }
}
