using System;
using Game.Data.Space;
using Game.Systems.Ships;

namespace Game.Systems.Quests {
    public interface IPreCheckedQuestStep {
        void PreCheck();
    }
}
