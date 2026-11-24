namespace Game.Systems.Atmo {
    // Resistance thresholds and the write floor shared by the atmosphere code.
    // Resistance is the per-cell airtightness (O2 layer) or insulation (Temp layer)
    // written by AtmoSys.OnRecalculateAtmosphere from HullIntegrityComp.
    public static class AtmoRes {
        // Below this there is no hull under the cell: open space, or a floor damaged
        // to under half. Such a cell never takes gas from its neighbours, it only
        // bleeds what it holds by the layer's LossInSpace, once per step
        public const float Space = 0.025f;
        // At or above this nothing gets through and the cell always reads 0. Walls
        // insulate heat at 0.995, so for the Temp layer they sit below this and conduct
        public const float Solid = 0.9999f;
        // Results at or below this are written as 0 so empty areas stay exactly empty.
        // It is a mass sink on the leading edge of a diffusion front, and the thinner
        // gas is spread the more it eats, so it has to be far below anything a player
        // can notice: this is five parts per million of breathable oxygen
        public const float MinValue = 0.000001f;

        // Players read airtightness and insulation as how much a tile keeps in. A floor's
        // resistance only sets how freely gas spreads across it inside a room, so its raw
        // 5% read as "barely holds air". Sealing-wise a floor either holds its cell against
        // space or it does not, and that flips at Space, so floors show 100% or 0%
        public static float ForDisplay(float res, bool isFloor) {
            if (!isFloor) { return res; }
            return res >= Space ? 1f : 0f;
        }
    }
}
