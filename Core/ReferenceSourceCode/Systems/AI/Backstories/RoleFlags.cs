using System;

namespace Game.Systems.Backstories {
    [Flags]
    public enum RoleFlags : byte {
        None = 0,
        Dumb = 1 << 0,
        Smart = 1 << 1,
        Poor = 1 << 2,
        Rich = 1 << 3,
        Good = 1 << 4,
        Evil = 1 << 5,
        /// <summary>
        /// once per lifetime for anyone
        /// </summary>
        Once = 1 << 6
    }
}
