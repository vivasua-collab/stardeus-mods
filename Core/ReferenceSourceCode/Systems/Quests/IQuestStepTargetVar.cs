namespace Game.Systems.Quests {
    public interface IQuestStepTargetVar {
        /// <summary>
        /// Don't cache the output, same var can be used for multiple steps
        /// </summary>
        /// <param name="step"></param>
        /// <returns>new instance of QuestStepTarget for the given step</returns>
        QuestStepTarget GetTargetFor(QuestStep step);
    }
}
