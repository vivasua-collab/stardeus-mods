namespace Game.Systems.AI.Combat {
    // One attack's chance to hit and what it is made of, from AttackMethod.HitChance
    public readonly struct ShotOdds {
        // the weapon's accuracy at this distance (1 for melee: melee weapons have none)
        public readonly float Weapon;
        // multipliers: the attacker's ShootingAccuracy (at this distance) or MeleeAccuracy
        // (skill, eyes, hands, light), times OutnumberedOdds for a hostile fighting several of
        // the crew in the first raids; and 1 - the target's melee dodge
        public readonly float Being;
        // weapon and Being together: what the tooltip shows as one accuracy number
        public readonly float Aim;
        public readonly float Dodge;
        // multipliers: the target's cover toward the attacker, the target moving
        public readonly float Cover;
        public readonly float Moving;
        // a human nearby directs a bot's fire (CombatUnit.IsCommanded)
        public readonly float Leader;
        public readonly float Total;

        public ShotOdds(float weapon, float being, float aim, float dodge,
                float cover, float moving, float leader, float total) {
            Weapon = weapon;
            Being = being;
            Aim = aim;
            Dodge = dodge;
            Cover = cover;
            Moving = moving;
            Leader = leader;
            Total = total;
        }
    }
}
