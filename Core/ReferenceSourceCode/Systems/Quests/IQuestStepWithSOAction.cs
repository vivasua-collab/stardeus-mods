using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX;

namespace Game.Systems.Quests {
    public interface IQuestStepWithSOAction {
        bool HasSOActionFor(SpaceObject so);
        bool TryAddSOActionFor(SpaceObject so, QuickChoice qc);
    }
}
