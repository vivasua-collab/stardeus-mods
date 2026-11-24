using System;
using System.Collections.Generic;
using Game.Systems;
using KL.Grid;
using UnityEngine;

namespace Game.Visuals {
    // Edge tiles of the region covered by one or more ranges that are each
    // confined to their origin's area: the arc where a radius ends, plus the
    // tiles along the walls that cut it off. Several sources merge into the
    // edge of their combined coverage, so a selection of many floors reads as
    // one area instead of dozens of overlapping outlines. Used by
    // IRangedAreaOutline
    public sealed class RangeAreaOutline {
        private struct Source {
            public int OriginPos;
            public Vector2 Center;
            public float Range;
        }

        private readonly List<Source> sources = new();
        private bool[] covered = Array.Empty<bool>();

        public int SourceCount => sources.Count;

        public void Clear() {
            sources.Clear();
        }

        public void Add(int originPos, Vector2 center, float range) {
            if (range <= 0f) { return; }
            sources.Add(new Source {
                OriginPos = originPos,
                Center = center,
                Range = range,
            });
        }

        public void Collect(AreaSys areas, List<int> result) {
            result.Clear();
            if (sources.Count == 0) { return; }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            for (int i = 0; i < sources.Count; i++) {
                GetBounds(sources[i], out var sMinX, out var sMinY,
                    out var sMaxX, out var sMaxY);
                minX = Mathf.Min(minX, sMinX);
                minY = Mathf.Min(minY, sMinY);
                maxX = Mathf.Max(maxX, sMaxX);
                maxY = Mathf.Max(maxY, sMaxY);
            }
            if (minX > maxX || minY > maxY) { return; }
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            var size = w * h;
            if (covered.Length < size) {
                covered = new bool[size];
            } else {
                Array.Clear(covered, 0, size);
            }

            for (int i = 0; i < sources.Count; i++) {
                Mark(areas, sources[i], minX, minY, h);
            }

            for (int x = 0; x < w; x++) {
                for (int y = 0; y < h; y++) {
                    if (!covered[x * h + y]) { continue; }
                    if (!IsCovered(x - 1, y, w, h) || !IsCovered(x + 1, y, w, h)
                            || !IsCovered(x, y - 1, w, h) || !IsCovered(x, y + 1, w, h)) {
                        result.Add(Pos.FromXY(minX + x, minY + y));
                    }
                }
            }
        }

        private void Mark(AreaSys areas, Source s, int boxX, int boxY, int h) {
            GetBounds(s, out var minX, out var minY, out var maxX, out var maxY);
            var rangeSqr = s.Range * s.Range;
            for (int x = minX; x <= maxX; x++) {
                for (int y = minY; y <= maxY; y++) {
                    var idx = (x - boxX) * h + (y - boxY);
                    if (covered[idx]) { continue; }
                    // Tile centers against the range center, same as the plain ring
                    var dx = x + 0.5f - s.Center.x;
                    var dy = y + 0.5f - s.Center.y;
                    if (dx * dx + dy * dy > rangeSqr) { continue; }
                    if (!areas.IsSameAreaOrOutside(s.OriginPos, Pos.FromXY(x, y))) {
                        continue;
                    }
                    covered[idx] = true;
                }
            }
        }

        // Anything outside the box holds no covered tile
        private bool IsCovered(int x, int y, int w, int h) {
            if (x < 0 || y < 0 || x >= w || y >= h) { return false; }
            return covered[x * h + y];
        }

        private static void GetBounds(Source s, out int minX, out int minY,
                out int maxX, out int maxY) {
            minX = Mathf.Max(0, Mathf.FloorToInt(s.Center.x - s.Range));
            maxX = Mathf.Min(Pos.GridW - 1, Mathf.CeilToInt(s.Center.x + s.Range));
            minY = Mathf.Max(0, Mathf.FloorToInt(s.Center.y - s.Range));
            maxY = Mathf.Min(Pos.GridH - 1, Mathf.CeilToInt(s.Center.y + s.Range));
        }
    }
}
