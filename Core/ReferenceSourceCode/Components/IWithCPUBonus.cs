namespace Game.Components {
    // Implemented by devices whose effectiveness scales with the grid's CPU.
    // Returns the CPU-derived multiplier (1.0 = no bonus) used by
    // ElectricGrid.BonusTooltipFor to render the bonus line.
    public interface IWithCPUBonus {
        float GetCPUBonusMultiplier(out string statName, out string finalValue);
    }
}
