namespace Game.Systems.AI.Combat {
    public enum CombatUnitState : byte {
        // I have no idea there is combat going on, working on stuff
        Unaware,
        // I know about combat but I'm not giving a shit, working on stuff
        Aware,
        // I am trying to actively avoid enemies, hiding, etc
        Fleeing,
        // I am aggressive towards enemy, approaching, attacking, etc
        Aggressive,
        // Downed with pain, wounds, or dead
        Incapacitated,
    }
}
