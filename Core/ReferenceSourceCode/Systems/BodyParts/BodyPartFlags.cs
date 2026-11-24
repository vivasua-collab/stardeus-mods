using System;

namespace Game.Systems.BodyParts {
    [Flags]
    public enum BodyPartFlags : uint {
        None = 0,
        Removable = 1 << 1,
        Internal = 1 << 2,
        Limb = 1 << 3,
        Essential = 1 << 4,
        Biological = 1 << 5,
        HoldsMind = 1 << 6,
        /// <summary>
        /// Indestructible parts will not get severity >= Irreparable
        /// </summary>
        Indestructible = 1 << 7,
        /// <summary>
        /// If this part is missing, it won't count as missing
        /// </summary>
        Optional = 1 << 8,
        SupportsMindTransfer = 1 << 9,
        SupportsMLBooth = 1 << 10,
        /// <summary>
        /// If this flag is set, this body part will never be generated inside a being,
        /// so it can only be installed by the player or custom code. For example: Obey Chip
        /// </summary>
        NeverGenerate = 1 << 11,
    }
}
