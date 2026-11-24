using System.Collections.Generic;
using Game.Components;
using Game.Data;
using KL.Grid;

namespace Game.Procgen {
    // Where generation must not put anything in space: behind an engine, the flame heats
    // and damages whatever stands there in flight (ShipEngineComp.RadiateHeat), and solar
    // panels and particle collectors blow up on the first jump. One tile of margin all
    // round, because the flame looks wider than the tiles it heats.
    public static class EngineExhaust {
        public static void Collect(GameState S, ShipSpec spec, HashSet<int> into) {
            into.Clear();
            foreach (var engine in S.Components.GenFind<ShipEngineComp>()) {
                if (!spec.IsWithinGenArea(engine.Tile.PosIdx)) { continue; }
                if (!engine.TryGetFlameArea(out var area)) { continue; }
                area.ForEachExpanded((p) => into.Add(p), 1);
            }
        }

        // Whether any tile of a footprint at root is in the exhaust
        public static bool Overlaps(HashSet<int> exhaust, Tile proto, int root) {
            if (exhaust.Count == 0) { return false; }
            var w = proto.Transform.RotatedWidth;
            var h = proto.Transform.RotatedHeight;
            for (int x = 0; x < w; x++) {
                for (int y = 0; y < h; y++) {
                    if (exhaust.Contains(Pos.NeighborAt(root, x, y))) { return true; }
                }
            }
            return false;
        }
    }
}
