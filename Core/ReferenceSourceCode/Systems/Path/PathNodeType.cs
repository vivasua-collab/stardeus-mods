using System;

namespace Game.Systems.Path {
    [Flags]
    public enum PathNodeType : byte {
        None = 0,
        // walked across, never stopped on: a sandbag, a door
        PassThroughOnly = 1 << 0,
        Passable = 1 << 1,
        Walkable = 1 << 2,
        Door = 1 << 3,
        Teleporter = 1 << 4,
        Port = 1 << 5,
        Wall = 1 << 6,
        TileWithHeight = 1 << 7,
    }
}
