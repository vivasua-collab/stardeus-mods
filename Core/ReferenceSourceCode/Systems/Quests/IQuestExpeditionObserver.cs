using Game.Data;
using Game.Expeditions;

namespace Game.Systems.Quests {
    public interface IQuestExpeditionObserver {
        /// <param name="exp"></param>
        /// <param name="from"></param>
        /// <param name="to">will be null after expedition is done!</param>
        void OnExpeditionStageChange(Expedition exp,
            IExpeditionStage from, IExpeditionStage to);
    }
}
