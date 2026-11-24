using System;

namespace Game.Systems.Conditions {
    [Flags]
    public enum ConditionFlags : byte{
        None = 0,
        Inactive = 1 << 0,
        Undetected = 1 << 1,
        TickAfterDeath = 1 << 2,
        /// <summary>
        /// For example, crawler infections should not execute while being is on expedition
        /// </summary>
        PauseWhenBeingHidden = 1 << 3,
        /// <summary>
        /// If we resurrect a dead body, conditions with these flags will be cleared
        /// </summary>
        RemoveOnResurrect = 1 << 4,
        /// <summary>
        /// This flag is added if condition is in the ConditionSys
        /// </summary>
        RegisteredWithSystem = 1 << 5,
        /// <summary>
        /// Diseases that have immunity should have this flag set
        /// </summary>
        HasImmunity = 1 << 6,
        /// <summary>
        /// Reserved for marking immunities
        /// </summary>
        Immunity = 1 << 7,
    }
}
