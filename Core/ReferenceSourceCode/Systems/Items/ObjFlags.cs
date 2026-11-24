using System;

namespace Game.Systems.Items {
    /// <summary>
    /// Durable facts about an object's history that cannot be derived from its
    /// current state. Only gear worn on the body carries them - see
    /// ItemOwnership.TracksOwnership.
    /// </summary>
    /// <see cref="ItemOwnership"/>
    [Flags]
    public enum ObjFlags : byte {
        None = 0,
        /// <summary>
        /// The item has changed hands at least once. Never cleared - otherwise
        /// unequipping and re-equipping would launder it.
        /// </summary>
        SecondHand = 1 << 0,
        /// <summary>
        /// The item came off a being that was dead at the time. Cleared only when
        /// that same owner puts it back on after being resurrected.
        /// </summary>
        BelongedToDead = 1 << 1,
    }
}
