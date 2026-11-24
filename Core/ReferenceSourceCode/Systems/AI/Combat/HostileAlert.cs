namespace Game.Systems.AI.Combat {
    // Where a hostile fight stands for the player (CombatAlerts). Saved, and 0 is what saves
    // made before the stages load as: their alert was up.
    public enum HostileAlert : byte {
        // first contact happened: Combat in progress
        InCombat = 0,
        // no enemy who can reach us has advanced yet: nobody is told
        Quiet = 1,
        // an enemy who can reach us is on its way: the raid alert
        Raid = 2,
    }
}
