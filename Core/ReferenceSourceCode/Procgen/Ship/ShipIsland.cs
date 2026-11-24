using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using KL.Grid;
using UnityEngine;

namespace Game.Procgen {
    [Serializable]
    public struct ShipIsland {
        public List<int> Cells;
        public PosRect AABB;
        public ShipIsland(ConcurrentBag<int> cells) {
            Cells = new List<int>(cells);
            Cells.Sort();

            Pos.ToXY(Cells[0], out var x, out var y);
            var minX = x;
            var minY = y;
            var maxX = x;
            var maxY = y;
            foreach (var cell in cells) {
                Pos.ToXY(cell, out var x1, out var y1);
                minX = Mathf.Min(minX, x1);
                maxX = Mathf.Max(maxX, x1);
                minY = Mathf.Min(minY, y1);
                maxY = Mathf.Max(maxY, y1);
            }
            AABB = new PosRect(minX, minY, maxX, maxY);
        }

        public List<int> OutermostCells {
            get {
                var list = new List<int>();

                foreach (var cell in Cells) {
                    Pos.ToXY(cell, out var x, out var y);
                    if (x == AABB.MinX || x == AABB.MaxX ||
                            y == AABB.MinY || y == AABB.MaxY) {
                        list.Add(cell);
                    }
                }
                return list;
            }
        }

        public int Size => Cells.Count;
        public override string ToString() {
            return string.Format("Island(x{0})[{1}]", Size, Pos.String(Cells));
        }
    }
}
