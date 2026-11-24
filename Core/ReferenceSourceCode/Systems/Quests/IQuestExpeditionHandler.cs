using Game.Data;
using Game.Expeditions;

namespace Game.Systems.Quests {
    public interface IQuestExpeditionHandler {
        bool IsExpeditionStageCancellable { get; }
        IExpeditionStage RunExpedition(Expedition exp, ExpeditionStageQuest stage, long ticks);
    }
}
