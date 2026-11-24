namespace Game.Systems.Ships {
    /// <summary>
    /// Why ShipCombatAI.ResolveFocus was called. Decides how eagerly a ship is
    /// allowed to pick a new target.
    /// </summary>
    public enum FocusResolveReason {
        /// <summary>
        /// Regular combat tick: only reselect when the current target is gone
        /// </summary>
        Tick,
        /// <summary>
        /// A ship joined or left one of the sides
        /// </summary>
        RosterChange,
        /// <summary>
        /// The current target has been held for as long as the strategy wanted.
        /// Detected inside the tick path, never passed in from outside.
        /// </summary>
        IntervalExpired,
        /// <summary>
        /// Restoring a saved combat: weapon targets were just deserialized and
        /// must not be cleared
        /// </summary>
        Restore,
    }
}
