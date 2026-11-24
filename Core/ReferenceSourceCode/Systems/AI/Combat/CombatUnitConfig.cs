namespace Game.Systems.AI.Combat {
    /// <summary>
    /// What does the player want the unit to do when combat happens
    /// </summary>
    public enum CombatUnitConfig : byte {
        FreeWill,
        Fight,
        Flee,
        Ignore,
    }
}
