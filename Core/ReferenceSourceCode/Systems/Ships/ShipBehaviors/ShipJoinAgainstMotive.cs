namespace Game.Systems.Ships {
    /// <summary>
    /// Why an NPC ship would fly in and fight against the player. Doubles as the
    /// admission priority for the limited enemy slots: Police, then SameFaction,
    /// then Ally. See ShipJoinMotive for the mirrored "join for the player" case.
    /// </summary>
    public enum ShipJoinAgainstMotive {
        None = 0,
        /// <summary>Law enforcement defending a lawful ship from an outlaw player.</summary>
        Police = 1,
        /// <summary>Same faction as a ship the player is fighting.</summary>
        SameFaction = 2,
        /// <summary>Their faction is allied with a ship the player is fighting.</summary>
        Ally = 3,
    }
}
