namespace Game.Systems.Ships {
    public interface IShipDevice {
        ShipDevice Device { get; }
        string PhaseName { get; }
        string PhaseIcon { get; }
        float PhasePercent { get; }
        bool PhaseHasProgress { get; }
        /// <summary>
        /// Disabled is still active!
        /// Inactive = disconnected or turned off if it's player's device.
        /// </summary>
        bool IsActive { get; }
        void UpdateHealth();

        float CurrentEnergyCost { get; }
        string FullTooltip { get; }
    }
}
