using System;

namespace Game.Systems.Drama {
    [Flags]
    public enum BeingOpinionFlags {
        None = 0,
        Acquaintance = 1 << 1,
        /// <summary>
        /// Only when Friendship is high and reciprocal for both
        /// </summary>
        Friend = 1 << 2,
        /// <summary>
        /// Only when Love is high (not necessarily reciprocal)
        /// </summary>
        Crush = 1 << 3,
        /// <summary>
        /// Only when Love is high and reciprocal for both
        /// </summary>
        Lover = 1 << 4,
        /// <summary>
        /// Only when Hate is high
        /// </summary>
        Nemesis = 1 << 5,
        /// <summary>
        /// Owner of a pet (or maybe a slave)
        /// </summary>
        Master = 1 << 6,
        /// <summary>
        /// Is a personal pet of someone
        /// </summary>
        Pet = 1 << 7,
        /// <summary>
        /// Married couple
        /// </summary>
        Spouse = 1 << 8,
        /// <summary>
        /// Relationships that can only exist if reciprocal
        /// </summary>
        Reciprocal = Friend | Lover | Spouse,
        /// <summary>
        /// Relationships that are only one to one
        /// </summary>
        OneToOne = Master | Pet | Spouse,
        StalkerTarget = Master | Spouse | Lover | Nemesis | Crush,
        /// <summary>
        /// For mourning dead beings with opinions that have these flags
        /// </summary>
        Mournable = Master | Spouse | Lover | Crush | Friend,
        /// <summary>
        /// These will be cached in Mind.Bonds.Important dictionary for quick access.
        /// If this is changed, update BeingBonds.importantFlags too!
        /// </summary>
        Important = Friend | Crush | Lover | Nemesis | Master | Pet | Spouse,
    }
}
