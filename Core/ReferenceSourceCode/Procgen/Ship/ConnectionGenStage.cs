using System.Collections.Generic;
using Game.Constants;
using Game.Procgen;
using KL.Grid;
using UnityEngine;

namespace Game.Procgen {
    public class ConnectionGenStage : PreviewBaseShipGenStage {
        public override void Apply() {
            var numVerticalConnections = rng.Range(0, 5);
            var numHorConnections = rng.Range(0, 4);
            float vertStep = 1f / numVerticalConnections;
            float horStep = 1f / numHorConnections;
            var rows = new List<int>();
            var cols = new List<int>();
            for (float i = 1; i < spec.TemplateWidth - 1;
                    i += spec.TemplateWidth * vertStep) {
                cols.Add(Mathf.FloorToInt(i));
            }
            for (float i = 1; i < spec.TemplateHeight - 1;
                    i += spec.TemplateHeight * horStep) {
                rows.Add(Mathf.FloorToInt(i));
            }

            var list = new List<int>();

            foreach (var row in rows) {
                for (int i = 0; i < spec.TemplateHeight; i++) {
                    if (spec.IsCell(row, i, CellType.BodyEmpty) ||
                            spec.IsCell(row, i, CellType.CockpitEmpty)) {
                        spec.SetCell(row, i, CellType.Connection);
                    }
                }
            }

            foreach (var col in cols) {
                for (int i = 0; i < spec.TemplateWidth; i++) {
                    if (spec.IsCell(i, col, CellType.BodyEmpty) ||
                            spec.IsCell(i, col, CellType.CockpitEmpty)) {
                        spec.SetCell(i, col, CellType.Connection);
                    }
                }
            }

            spec.ForEachMirrored((x, y, type) => {
                if (type == CellType.Connection) {
                    var n = spec.IsEmpty(x, y + 1, true);
                    var s = spec.IsEmpty(x, y - 1, true);
                    var e = spec.IsEmpty(x + 1, y, true);
                    var w = spec.IsEmpty(x - 1, y, true);
                    var ne = spec.IsEmpty(x + 1, y + 1, true);
                    var se = spec.IsEmpty(x - 1, y + 1, true);
                    var nw = spec.IsEmpty(x + 1, y - 1, true);
                    var sw = spec.IsEmpty(x - 1, y - 1, true);
                    var trimX = e && w ? 1 : 0;
                    var trimY = n && s ? 1 : 0;
                    if (spec.CellWidth <= 7 || spec.CellHeight <= 7) {
                        trimX = trimY = 0;
                    }
                    if (!ne || !nw || !se || !sw) {
                        trimX = 0;
                        trimY = 0;
                    }
                    AppendAdjustedPosition(x, y, trimX, trimY, list);
                }
            });

            BuildTiles(list, DefIds.StructureFloorsFloor01);
        }
    }
}
