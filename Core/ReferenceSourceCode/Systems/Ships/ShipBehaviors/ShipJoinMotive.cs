namespace Game.Systems.Ships {
    /// <summary>
    /// Why an NPC ship would fly in and fight on the player's side. Doubles as the
    /// admission priority for the limited ally slots: Police, then Ally, then
    /// CommonEnemy.
    /// </summary>
    public enum ShipJoinMotive {
        None = 0,
        /// <summary>Law enforcement responding to pirates. Predates alliances.</summary>
        Police = 1,
        /// <summary>Their faction is allied with the player.</summary>
        Ally = 2,
        /// <summary>They are at war with someone the player is fighting.</summary>
        CommonEnemy = 3,
    }
}
