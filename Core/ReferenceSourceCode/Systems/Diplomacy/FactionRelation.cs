namespace Game.Systems.Diplomacy {
    /// <summary>
    /// Symmetric diplomatic state between two factions. Stored separately from the
    /// directional attitude floats in DiplomacySys.
    /// </summary>
    public enum FactionRelation {
        Neutral = 0,
        Allied = 1,
        Enemy = 2,
    }
}
