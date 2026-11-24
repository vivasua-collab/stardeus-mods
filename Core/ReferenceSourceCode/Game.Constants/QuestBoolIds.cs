namespace Game.Constants {
    public static class QuestBoolIds {
        public const string ReadyForTutorialQuests = nameof(ReadyForTutorialQuests);
        public const string DisallowApproachDerelict = nameof(DisallowApproachDerelict);
        public const string UsedDarkPortal = nameof(UsedDarkPortal);
        /// <summary>
        /// Set by a scenario intro when nobody in the starting crew can run the
        /// operator-required Crafting Station (QuestUtils.MarkCrewCannotCraftIfNeeded). The
        /// Crafting job is ability-driven, and a stock robot has the abilities through its
        /// Manipulator hand, so this is a fallback rather than the robot-start default. Swaps
        /// the production tutorial for the automated Assembler variant. See
        /// Config/Quests/Quests/Tutorial/EstablishAutomatedProduction.json.
        /// </summary>
        public const string CrewCannotCraft = nameof(CrewCannotCraft);
        // Set by MakeStasisAreaSurvivable (on completion or failure). Releases the first
        // stasis wake-up on a ship that starts without colonists (StasisWakeUpStoryAspect).
        public const string MadeStasisSurvivable = nameof(MadeStasisSurvivable);
        // Tutorial chain milestones the HUD reveal and waiting suggestions key off. Set by the
        // tutorial quests' SetOnComplete / SetOnFail (Config/Quests/Quests/Tutorial).
        public const string HasChargeStation = nameof(HasChargeStation);
        public const string HasEstablishedProduction = nameof(HasEstablishedProduction);
        public const string HasEstablishedResearch = nameof(HasEstablishedResearch);
        public const string ConnectedToStasis = nameof(ConnectedToStasis);
        public const string HasConnectedToSurvivorArea = nameof(HasConnectedToSurvivorArea);
        public const string EnclosedStasis = nameof(EnclosedStasis);
        public const string HasRepairStation = nameof(HasRepairStation);
        public const string HasBeds = nameof(HasBeds);
        public const string HasFoodProduction = nameof(HasFoodProduction);
        public const string HasHygiene = nameof(HasHygiene);
        public const string HasRadar = nameof(HasRadar);
        public const string HadExpedition = nameof(HadExpedition);
        // Set when the first tutorial quest is offered, or skipped (QuestActivator,
        // QuestSys.SkipTutorials). The build menu's Electricity tab waits for it.
        public const string TutorialQuestOffered = nameof(TutorialQuestOffered);
        // Set by QuestSys.SkipTutorials before anything else: what the skip reveals rings nothing
        public const string SkippedTutorial = nameof(SkippedTutorial);
        // Set on a new game: only runs started with the HUD reveal hide anything
        // (HudRevealRules). Older saves keep the full HUD.
        public const string HudRevealActive = nameof(HudRevealActive);
        // Everything is revealed for good: every wave is in, or Show Hints was off.
        public const string HudRevealDone = nameof(HudRevealDone);
    }
}
