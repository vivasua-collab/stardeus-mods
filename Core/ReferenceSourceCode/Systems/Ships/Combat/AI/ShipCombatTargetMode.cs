namespace Game.Systems.Ships {
    /// <summary>
    /// How a ship decides which of the opposing ships to attack.
    /// Configured per strategy in ShipCombatTargetStrategyDef.
    /// </summary>
    public enum ShipCombatTargetMode {
        /// <summary>
        /// The opponent with the most firepower it can currently use
        /// </summary>
        Strongest,
        /// <summary>
        /// The opponent with the least firepower it can currently use
        /// </summary>
        Weakest,
        /// <summary>
        /// Enemy side only: the player's ship, ignoring its allies
        /// </summary>
        Primary,
        /// <summary>
        /// Enemy side only: the player's allies while any are alive, then the player
        /// </summary>
        Support,
        /// <summary>
        /// A random opponent, changed whenever the switch interval runs out
        /// </summary>
        Fickle,
        /// <summary>
        /// A random opponent, kept until it leaves the fight
        /// </summary>
        Sticky,
    }
}
