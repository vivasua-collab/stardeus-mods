using System;

namespace Game.Systems.Path {
    [Flags]
    public enum Layers {
        None = 0,
        Floor = 1,
        Wall = 2,
        Obj = 4,
    }
}
