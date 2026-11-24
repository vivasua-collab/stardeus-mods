namespace Game.Systems.Ships {
    /// <summary>
    /// Inactive -> Charging -> Active
    /// Disabled -> Charging - shield slowly discharge when disabled
    /// </summary>
    public enum ShipShieldPhase {
        Undefined,
        Inactive,
        Charging,
        Active,
        Disabled,
    }
}
