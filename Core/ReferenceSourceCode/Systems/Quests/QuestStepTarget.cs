using System;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Ships;

namespace Game.Systems.Quests {
    public sealed class QuestStepTarget {
        public IQuestProvider Target;
        public QuestStep Step;
        // for signals
        public Action OnInteract;
        public bool KeepSOOnInteract;
        public IQuestExpeditionObserver ExpeditionObserver;
        public IQuestExpeditionHandler ExpeditionHandler;
        /// <summary>
        /// Mind id of a being that must be locked into the crew of an expedition to this
        /// target (e.g. a delivery quest passenger). 0 = none. Read by
        /// ExpeditionContext.AutofillCrew.
        /// </summary>
        public uint RequiredCrewMindId;
        public bool IsRegistered;
    }
}
