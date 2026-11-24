using System;

namespace Game.Systems.Naming {
    [Flags]
    public enum NameType : byte {
        None = 0,
        Basic = 1 << 1,
        Seed = 1 << 2,
        Being = 1 << 3,
        SpaceObject = 1 << 4,
        Ship = 1 << 5,
        SpaceMapObject = 1 << 6,
        ShipOS = 1 << 7,
    }
}
