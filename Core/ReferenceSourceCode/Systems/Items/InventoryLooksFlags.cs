using System;

namespace Game.Systems.Items {
    [Flags]
    public enum InventoryLooksFlags : byte {
        None = 0,
        /// <summary>
        /// We don't want to show the space suit when the colonist is in warm
        /// oxygenated environment.
        /// </summary>
        SafeEnv = 1,
        Combat = 2,
        Sleeping = 4,
        /// <summary>
        /// If SpaceSuit is hiding Body slot, but there is nothing equipped on
        /// that slot, when the env is safe, we still want to show the suit.
        /// </summary>
        SlotEmpty = 8,
    }
}
