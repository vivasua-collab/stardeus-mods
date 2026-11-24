using Game.Data;

namespace Game.Systems.Quests {
    public interface IQuestValidator {
        string Type { get; }
        bool CanStartQuest(GameState s, QuestDef def);
    }
}
