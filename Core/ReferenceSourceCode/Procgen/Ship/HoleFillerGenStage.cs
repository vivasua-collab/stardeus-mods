using System.Collections.Generic;
using Game.Procgen;
using KL.Grid;
using Game.Data;
using System.Linq;
using UnityEngine;
using Game.Constants;

// 122748929

namespace Game.Procgen {
    public class HoleFillerGenStage : PreviewBaseShipGenStage {
        ///<summary>Check if there are 3 hor/ver neighbors, and if so, fill the
        ///hole</summary>
        public override void Apply() {
            if (spec.IsAsymmetric) {
                ApplyFull();
                return;
            }
            var holes = new List<int>();
            var specHoles = new List<XY>();

            spec.ForEachMirrored((x, y, type) => {
                if (!spec.IsEmpty(x, y)) { return; }
                var count = FindNeighborCount(x, y, false);
                //var countD = FindNeighborCount(x, y, true);
                if (count == 3 && HasDiagonalGap(x, y)) {
                    spec.SetCell(x, y, CellType.Skin);
                    specHoles.Add(new(x, y));
                    AppendAdjustedPositionMirrored(x, y, holes);
                }
            });

            foreach (var hole in specHoles) {
                //if (rng.Chance(0.8f)) {
                    spec.SetCell(hole.X, hole.Y, CellType.Skin);
                //}
            }

            var randDef = The.Defs.RandomMatching(
                DefIds.StructureFloorsFloor01, spec.CosmeticRng);

            BuildTiles(holes, randDef.Id);
        }

        private void ApplyFull() {
            var holes = new List<int>();
            var specHoles = new List<XY>();
            for (int x = 0; x < spec.TemplateWidth; x++) {
                for (int fy = 0; fy < spec.FullHeight; fy++) {
                    if (!spec.IsEmptyFull(x, fy)) { continue; }
                    int count = 0;
                    if (!spec.IsEmptyFull(x, fy + 1, false)) { count++; }
                    if (!spec.IsEmptyFull(x, fy - 1, false)) { count++; }
                    if (!spec.IsEmptyFull(x + 1, fy, false)) { count++; }
                    if (!spec.IsEmptyFull(x - 1, fy, false)) { count++; }
                    if (count != 3) { continue; }
                    var ne = spec.IsEmptyFull(x + 1, fy + 1);
                    var se = spec.IsEmptyFull(x + 1, fy - 1);
                    var nw = spec.IsEmptyFull(x - 1, fy + 1);
                    var sw = spec.IsEmptyFull(x - 1, fy - 1);
                    if ((ne && sw) || (nw && se)) {
                        specHoles.Add(new(x, fy));
                    }
                }
            }
            foreach (var hole in specHoles) {
                spec.SetFull(hole.X, hole.Y, CellType.Skin);
                AppendAdjustedPosition(hole.X, hole.Y, holes);
            }
            var randDef = The.Defs.RandomMatching(
                DefIds.StructureFloorsFloor01, spec.CosmeticRng);
            BuildTiles(holes, randDef.Id);
        }

        private bool HasDiagonalGap(int x, int y) {
            var ne = spec.IsEmpty(x + 1, y + 1);
            var se = spec.IsEmpty(x + 1, y - 1);
            var nw = spec.IsEmpty(x - 1, y + 1);
            var sw = spec.IsEmpty(x - 1, y - 1);
            return (ne && sw) || (nw && se);
        }
    }
}
