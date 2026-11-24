using System;

namespace Game.Systems.Traits {
    [Flags]
    public enum TraitFlags : byte {
        None = 0,
        /// <summary>
        /// The trait is only available for someone with a role that assigns that trait
        /// </summary>
        RoleOnly = 1 << 1,
        /// <summary>
        /// Physical traits will remain with the body they were generated for.
        /// When mind gets removed, all physical traits will be transferred into
        /// the blank mind that gets set instead of previous mind, and vice versa
        /// </summary>
        BodyBound = 1 << 2,
    }
}
