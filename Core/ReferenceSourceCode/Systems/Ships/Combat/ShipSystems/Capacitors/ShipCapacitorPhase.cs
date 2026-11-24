namespace Game.Systems.Ships {
    /// <summary>
    /// Inactive -> Charging -> Active
    /// Disabled -> Charging - capacitor slowly discharges when disabled
    /// </summary>
    public enum ShipCapacitorPhase {
        Undefined,
        Inactive,
        Charging,
        Active,
        Disabled,
    }
}
