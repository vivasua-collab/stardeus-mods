using System;

namespace Game.Systems.Items {
    [Flags]
    public enum GenItemFlags {
        None = 0,
        Casual = 1 << 1,
        SpaceReady = 1 << 2,
        Weaponized = 1 << 3,
        /// <summary>
        /// SpaceReady may include jet suit anyway, but this can be used to explicitly ask
        /// for a jet suit in early game
        /// </summary>
        JetSuit = 1 << 4,
        /// <summary>
        /// Biases weapon/armor rolls toward higher CombatStrengthBonus/DPS items and raises
        /// the generated item's quality floor. Used by CombatRaid to make wealthier raids
        /// field better-equipped raiders instead of just more of them.
        /// </summary>
        WellArmed = 1 << 5,
        /// <summary>
        /// Stronger version of WellArmed: weights weapon/armor rolls toward CombatValue more
        /// aggressively (see ObjsSys.GenerateInventoryItem), and raises the quality floor further.
        /// </summary>
        EliteArmed = 1 << 6,
    }
}
