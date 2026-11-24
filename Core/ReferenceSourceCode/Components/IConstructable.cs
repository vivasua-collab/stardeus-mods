using Game.Data;
using Game.Systems.AI;

namespace Game.Components {
    public interface IConstructable : IComponent, IMatRequester,
            IAIGoalProvider {
        bool IsFinished { get; }
        Tile ConstructableTile { get; }
        // Mostly for story event hooks, i.e. construction disaster
        AIAgentComp ConstructedBy { get; }
        bool AddWork(AIAgentComp builder, float amt);
    }
}
