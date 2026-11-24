using System;

namespace Game.Data.Space {
    [Flags]
    public enum SOFlags {
        None = 0,
        /// <summary>
        /// Player can terraform it
        /// </summary>
        Terraformable = 1 << 0,
        /// <summary>
        /// Someone else has terraformed it already
        /// </summary>
        Habitable = 1 << 1,
        Settled = 1 << 2,
        Visited = 1 << 3,
        VisitedHS = 1 << 4
    }

}
