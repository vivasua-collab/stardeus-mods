namespace Game.Systems.Ships {
    public enum ShipWeaponType {
        Undefined,
        /// <summary>
        /// Machine guns, small caliber stuff.
        /// Blocked by shields (not 100%). Medium heat cost.
        /// </summary>
        Ballistic,
        /// <summary>
        /// Goes through shields, high heat cost. requires expensive ammo.
        /// </summary>
        Missile,
        /// <summary>
        /// Fast fire, blocked by shields, small damage, small heat cost.
        /// </summary>
        Laser,
        /// <summary>
        /// Disrupts shields on first hit. Slow firing. Does system damage if shields are down.
        /// High heat cost.
        /// </summary>
        Plasma,
        PointDefense,
    }
}
