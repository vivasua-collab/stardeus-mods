using System;
using System.Collections.Generic;
using Game.Constants;
using KL.Grid;

namespace Game.Systems {
    // Which tiles lie within Tunables.AutoHaulIslandReach tiles (Chebyshev) of an island,
    // in space or on any floor. Storages auto-haul only what is in reach of their own
    // island (AutoHaulReach), and they ask for every candidate pile, all game, so the answer
    // is a lookup in a mask built once per island and kept until the islands change.
    //
    // A mask covers the island's bounding box grown by the reach. It is a separable dilation
    // (rows, then columns, each a sliding window over prefix counts), which costs a few
    // passes over that box and nothing per query. Only islands that hold an auto-hauling
    // storage ever get one. The arrays are reused across rebuilds. A full recalculation of
    // the areas follows every wall or door built, and it may renumber the islands without
    // changing their tiles: AreaSys then calls Remap instead of Invalidate.
    public sealed class IslandReach {
        private sealed class Mask {
            public int MinX;
            public int MinY;
            public int Width;
            public int Height;
            // A tile of the island, to find it again when the islands are renumbered
            public int SamplePos;
            public bool[] Bits = Array.Empty<bool>();

            public bool Contains(int x, int y) {
                x -= MinX;
                y -= MinY;
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return false; }
                return Bits[y * Width + x];
            }
        }

        private readonly AreaSys areas;
        private Dictionary<int, Mask> masks = new();
        private Dictionary<int, Mask> remapped = new();
        private readonly Stack<Mask> spare = new();
        private bool[] source = Array.Empty<bool>();
        private bool[] rows = Array.Empty<bool>();
        private int[] prefix = Array.Empty<int>();

        // Masks built so far, for tests that hold the cache to its promise
        public int Builds { get; private set; }

        public IslandReach(AreaSys areas) {
            this.areas = areas;
        }

        public void Invalidate() {
            foreach (var mask in masks.Values) {
                spare.Push(mask);
            }
            masks.Clear();
        }

        // After a recalculation that kept every island's tiles but may have renumbered them
        public void Remap() {
            remapped.Clear();
            foreach (var kv in masks) {
                var mask = kv.Value;
                var id = mask.Width > 0 ? areas.IslandAt(mask.SamplePos) : 0;
                if (id == 0 || !remapped.TryAdd(id, mask)) {
                    spare.Push(mask);
                }
            }
            (masks, remapped) = (remapped, masks);
            remapped.Clear();
        }

        // Island 0 is no island: nothing to measure from, so nothing is out of reach
        public bool IsWithin(int islandId, int pos) {
            if (islandId == 0) { return true; }
            if (!masks.TryGetValue(islandId, out var mask)) {
                mask = Build(islandId);
                masks[islandId] = mask;
            }
            Pos.ToXY(pos, out var x, out var y);
            return mask.Contains(x, y);
        }

        private Mask Build(int islandId) {
            Builds++;
            var mask = spare.Count > 0 ? spare.Pop() : new Mask();
            mask.Width = 0;
            mask.Height = 0;
            if (!areas.Islands.TryGetValue(islandId, out var island) || island.Count == 0) {
                return mask;
            }
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var p in island) {
                mask.SamplePos = p;
                Pos.ToXY(p, out var x, out var y);
                if (x < minX) { minX = x; }
                if (y < minY) { minY = y; }
                if (x > maxX) { maxX = x; }
                if (y > maxY) { maxY = y; }
            }
            var reach = Math.Max(0, Tunables.AutoHaulIslandReach);
            mask.MinX = minX - reach;
            mask.MinY = minY - reach;
            var w = mask.Width = maxX - minX + 1 + 2 * reach;
            var h = mask.Height = maxY - minY + 1 + 2 * reach;
            var size = w * h;
            Ensure(ref source, size);
            Ensure(ref rows, size);
            Ensure(ref mask.Bits, size);
            Ensure(ref prefix, Math.Max(w, h) + 1);
            Array.Clear(source, 0, size);
            foreach (var p in island) {
                Pos.ToXY(p, out var x, out var y);
                source[(y - mask.MinY) * w + (x - mask.MinX)] = true;
            }
            // Rows: a tile is in reach along x when any island tile is within reach of it
            for (int y = 0; y < h; y++) {
                var row = y * w;
                prefix[0] = 0;
                for (int x = 0; x < w; x++) {
                    prefix[x + 1] = prefix[x] + (source[row + x] ? 1 : 0);
                }
                for (int x = 0; x < w; x++) {
                    var from = Math.Max(0, x - reach);
                    var to = Math.Min(w, x + reach + 1);
                    rows[row + x] = prefix[to] - prefix[from] > 0;
                }
            }
            // Columns over the row result: in reach along both axes, the Chebyshev square
            for (int x = 0; x < w; x++) {
                prefix[0] = 0;
                for (int y = 0; y < h; y++) {
                    prefix[y + 1] = prefix[y] + (rows[y * w + x] ? 1 : 0);
                }
                for (int y = 0; y < h; y++) {
                    var from = Math.Max(0, y - reach);
                    var to = Math.Min(h, y + reach + 1);
                    mask.Bits[y * w + x] = prefix[to] - prefix[from] > 0;
                }
            }
            return mask;
        }

        private static void Ensure<T>(ref T[] array, int size) {
            if (array.Length < size) {
                array = new T[size];
            }
        }
    }
}
