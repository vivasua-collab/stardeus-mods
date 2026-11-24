namespace Game.Systems.Story {
    public enum StoryPickIgnore {
        None = 0,
        Schedule = 1 << 0,
        RepeatDelay = 1 << 1,
        Points = 1 << 2,
        /// <summary>
        /// Except points!
        /// </summary>
        All = Schedule | RepeatDelay,
        /// <summary>
        /// Used by the hook fallback pass. Unlike All, this also ignores points.
        /// </summary>
        Fallback = Schedule | RepeatDelay | Points,
        /// <summary>
        /// Skips the seeded sector roll (SectorDifficultyChance). It gives random picks their
        /// sector flavor, and a scripted start must not be vetoed by the sector it happens in.
        /// </summary>
        SectorDifficulty = 1 << 3,
    }
}
