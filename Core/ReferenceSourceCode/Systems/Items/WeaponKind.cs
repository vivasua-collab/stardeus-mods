namespace Game.Systems.Items {
    // Classification of an equippable weapon, derived from its attack methods.
    // Matches the Melee/Shooting split InventoryItemDef.RequiredJobId already uses.
    public enum WeaponKind : byte {
        Any = 0,
        Melee = 1,
        Ranged = 2,
    }
}
