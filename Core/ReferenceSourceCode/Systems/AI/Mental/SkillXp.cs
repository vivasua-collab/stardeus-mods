namespace Game.Systems.Mental {
    // Skill XP per tick of an activity, on the scale of Skills/XpToNextLevel (1000 XP for the
    // first level). The rates were set per game hour of the activity (an hour is 60 ticks)
    // and are kept here, in one place, so they can be compared and tuned side by side. Loves
    // and the daily cap apply in JobSlot.TrainSkill. Docs/Architecture/Systems/SkillsAndLeveling.md
    // says why.
    public static class SkillXp {
        // XP per game hour -> XP per tick
        private const float PerHour = 1f / 60f;

        // Activities without a rate of their own keep the pace they had before the XP scale
        // changed: one old unit is 1000 / 240 XP (level 0 needed 240 units, and needs 1000 XP)
        public const float Default = 1000f / 240f;
        // research and crafting at a bench; also device work, at the device's JobLevelUpSpeed
        public const float DeviceWork = 250f * PerHour;
        // construction; deconstruction and repairs alike. It used to be 6x this, so builders
        // maxed out in days
        public const float Construction = 625f * PerHour;
        // sowing and harvesting
        public const float Plants = 212.5f * PerHour;
        // kept at its old ratio to construction
        public const float Firefighting = Construction * 25f / 15f;
    }
}
