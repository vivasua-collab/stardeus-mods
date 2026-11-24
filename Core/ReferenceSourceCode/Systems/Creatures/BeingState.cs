using System;

namespace Game.Systems.Creatures {
    [Flags]
    public enum BeingState : byte {
        Uninitialized = 0,
        Alive = 1 << 0,
        Dead = 1 << 1,
        /// <summary>
        /// Mostly for face huggers, they get hidden while attached to a colonist
        /// </summary>
        Hidden = 1 << 2,
        Expedition = 1 << 3,
        Stored = 1 << 4,
        // Currently the being is loaded into some device
        InDevice = 1 << 5,
        InColony = 1 << 6,
        IncapableOfCombat = Dead | Expedition | Stored | InColony,
    }
}
