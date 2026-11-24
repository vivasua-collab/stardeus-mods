using System;

namespace Game.Systems.AI.Actions {
    [Flags]
    public enum AIActionFlags {
        None = 0,
        Work = 1 << 0,
        Rest = 1 << 1,
        Quiet = 1 << 2,
        Combat = 1 << 3,
        /// <summary>
        /// Actions with mobile flag indicate worker moving around a lot.
        /// Certain social interactions should not happen when these actions
        /// are in progress.
        /// </summary>
        Mobile = 1 << 4,
        DoesNotRequireAbilities = 1 << 5,
        /// <summary>
        /// Skips agents that have robot minds or wild animals
        /// </summary>
        RequiresMentalHealth = 1 << 6,
    }
}
