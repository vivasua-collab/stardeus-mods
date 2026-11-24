namespace Game.Systems.Policies {
    public enum AutoEquipMode : byte {
        // Never auto-equip. The player equips this slot by hand.
        Manual = 0,
        // Only fill the slot when it is empty, never upgrade.
        IfEmpty = 1,
        // Keep looking for the best item allowed by the policy.
        Auto = 2,
    }
}
