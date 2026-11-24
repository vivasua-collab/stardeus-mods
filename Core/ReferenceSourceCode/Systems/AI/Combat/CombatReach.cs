using System.Collections.Generic;
using Game.Components;
using Game.Data;
using KL.Grid;

namespace Game.Systems.AI.Combat {
    // Which enemies can get to us at all. A walker on an island that holds neither the ship nor
    // anyone of ours is stranded: breaking walls does not take it across space, so it can never
    // reach anything of ours (STA-1716, a crawler left on a derelict). Islands are the test
    // because they are exactly that: connected tiles, with space between them.
    // An unknown ship, or a tile on no island (angled floors, fragments too small to be one),
    // counts as reachable, so nothing is called stranded by mistake.
    public sealed class CombatReach {
        private readonly CombatSys sys;
        private readonly HashSet<int> ourIslands = new();
        private int shipIsland;
        private int shipComputerPos = Pos.Invalid;
        private long shipTick = -1;
        private long islandsTick = -1;

        public CombatReach(CombatSys sys) {
            this.sys = sys;
        }

        // Both refreshes run once per tick at most, and callers in the same tick share them
        public void Refresh(long ticks) {
            RefreshShip(ticks);
            if (islandsTick == ticks) { return; }
            islandsTick = ticks;
            ourIslands.Clear();
            var areas = sys.S.Sys.Areas;
            var beings = sys.S.Beings.Alive;
            for (int i = 0; i < beings.Count; i++) {
                var b = beings[i];
                if (!b.IsActive || !b.Data.IsPlayer || b.Data.Body.IsIncapacitated) { continue; }
                var pos = b.PosIdx;
                if (Pos.IsOOB(pos)) { continue; }
                var island = areas.IslandAt(pos);
                if (island > 0) {
                    ourIslands.Add(island);
                }
            }
        }

        private void RefreshShip(long ticks) {
            if (shipTick == ticks) { return; }
            shipTick = ticks;
            shipIsland = sys.S.Sys.Areas.TryGetShipIsland(out var island) ? island : 0;
            shipComputerPos = ShipComputerComp.FindMain(sys.S)?.Tile.PosIdx ?? Pos.Invalid;
        }

        // How far the being is from the ship computer, or -1 without one. Asks for a Refresh in
        // the same tick first
        public float DistanceToShip(Being b) {
            if (shipComputerPos == Pos.Invalid || Pos.IsOOB(b.PosIdx)) { return -1f; }
            return Pos.Distance(b.PosIdx, shipComputerPos);
        }

        // Asks for a Refresh in the same tick first
        public bool IsStranded(Being b) {
            if (shipIsland <= 0 || !b.Motion.IsGrounded) { return false; }
            var pos = b.PosIdx;
            if (Pos.IsOOB(pos)) { return false; }
            var island = sys.S.Sys.Areas.IslandAt(pos);
            return island > 0 && island != shipIsland && !ourIslands.Contains(island);
        }

        // Structures of the ship are ours: bashing them is contact, a derelict's walls are not
        public bool IsOnShip(int pos, long ticks) {
            if (Pos.IsOOB(pos)) { return false; }
            RefreshShip(ticks);
            if (shipIsland <= 0) {
                return EntityUtils.IsWithinMargin(pos, false, false);
            }
            return sys.S.Sys.Areas.IslandAt(pos) == shipIsland;
        }
    }
}
