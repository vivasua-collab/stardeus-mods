namespace Game.Systems.Items {
    public enum ObjState : byte {
        // Not spawned yet, nor in storage
        Inactive,
        // Spawned Obj
        Active,
        // Belongs to a being inventory
        Equipped,
        // Stored in a storage unit
        Stored,
        // about to be wiped out from all systems, etc
        Destroyed,
        // loaded into device and something is happening to it. device is responsible
        // for managed the state
        InDevice,
    }
}
