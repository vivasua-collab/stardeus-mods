using System;

namespace Game.Systems.Drama {
    [Flags]
    public enum BeingClusterFlags {
        None = 0,
        HasDead = 1 << 1,
        HasEnemy = 1 << 2,
        HasFriendly = 1 << 3,
        /// <summary>
        /// Only if there is more than one sociable friendly being (not idle!)
        /// </summary>
        HasSociableGroup = 1 << 4,
        /// <summary>
        /// Only if there is more than one sociable IDLE friendly being
        /// </summary>
        HasSociableIdleGroup = 1 << 5,
        HasSleeping = 1 << 6,
        HasNoise = 1 << 7,
        HasActive = 1 << 8,
        // If this flag is set, it will only be listed in Expeditions.
        // One cluster per expedition, with ALL expedition members (including without mental health)
        Expedition = 1 << 9,
        DisturbsSleeping = HasSleeping | HasNoise,
        WantsCombat = HasEnemy | HasFriendly,
        SeenDead = HasActive | HasDead,
    }
}
