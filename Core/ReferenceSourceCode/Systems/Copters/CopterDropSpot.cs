using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.Copters {
    // Where a copter can set down what it carries, or park itself, when the planned spot
    // is taken. A random spot that was only checked once the copter had flown there kept a
    // copter hopping around the ship for game days (STA-1632), so the spot is checked here,
    // before the flight.
    public static class CopterDropSpot {
        // Square rings outward from `near`, so the first placeable position is one of the
        // closest. Runs once per failed drop, and at most hourly while nothing fits
        // (CoptersSys.TryFindDropSpot), so a full map search is affordable.
        public static bool TryFindNear(Def def, Facing.Type rot, int near, out int spot) {
            spot = 0;
            if (def == null || Pos.IsOOB(near)) { return false; }
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            if (proto == null) { return false; }
            proto.Transform.Rotation = rot;
            Pos.ToXY(near, out var cx, out var cy);
            var maxRing = Maths.Max(Pos.GridW, Pos.GridH);
            for (int r = 0; r < maxRing; r++) {
                for (int dx = -r; dx <= r; dx++) {
                    if (TryAt(proto, def, cx + dx, cy - r, ref spot)) { return true; }
                    if (r > 0 && TryAt(proto, def, cx + dx, cy + r, ref spot)) { return true; }
                }
                for (int dy = -r + 1; dy <= r - 1; dy++) {
                    if (TryAt(proto, def, cx - r, cy + dy, ref spot)) { return true; }
                    if (TryAt(proto, def, cx + r, cy + dy, ref spot)) { return true; }
                }
            }
            return false;
        }

        private static bool TryAt(Tile proto, Def def, int x, int y, ref int spot) {
            if (Pos.IsOOBXY(x, y)) { return false; }
            var pos = Pos.FromXY(x, y);
            if (!CmdPlaceTile.CanPlace(pos, proto, def.LayerId, warn: false)) { return false; }
            spot = pos;
            return true;
        }
    }
}
