using System;

namespace Game.Systems.Ships {
    [Flags]
    public enum ShipStateFlags {
        None = 0,
        /// <summary>
        /// Ship is alive and well
        /// </summary>
        Active = 1 << 1,
        /// <summary>
        /// Destroyed and turned into derelict
        /// </summary>
        Derelict = 1 << 2,
        /// <summary>
        /// Ship is in combat with another ship, won't be flying around or ticking
        /// </summary>
        InCombat = 1 << 3,
        /// <summary>
        /// Ship is player's game view, and shouldn't be visible in starfield
        /// </summary>
        InGameView = 1 << 4,
        /// <summary>
        /// Ship was turned into derelict by player or NPC so we have to spawn
        /// actual ship devices to match what we killed
        /// </summary>
        SpawnDevices = 1 << 5,
        /// <summary>
        /// Ship was defeated by the player specifically. Crew remnants of such a
        /// derelict are always hostile, while NPC-vs-NPC kills fall back to the
        /// victim faction's own hostility rules.
        /// </summary>
        DefeatedByPlayer = 1 << 6,
        /// <summary>
        /// The crew left the ship (a hack turned them), so the derelict is boarded
        /// without the crew remnants a combat kill leaves behind.
        /// </summary>
        CrewAbandoned = 1 << 7,
    }
}
