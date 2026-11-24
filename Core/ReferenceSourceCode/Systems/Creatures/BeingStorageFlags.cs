using System;

namespace Game.Systems.Creatures {
    [Flags]
    public enum BeingStorageFlags : byte {
        None = 0,
        Alive = 1,
        Dead = 2,
        Biological = 4,
        Robotic = 8,
        DeadOrAlive = Dead | Alive,
        BioOrRobo = Biological | Robotic,
    }
}
