using System;

namespace Game.Systems.BodyParts {
    [Flags]
    public enum WoundFlags : byte {
        None = 0,
        ///<summary>can't heal either in bed or in auto doc</summary>
        CantHeal = 1,
    }
}
