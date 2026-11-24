using Game.Components;

namespace Game.Systems.AI {
    public interface IAIGoalProvider : IComponent {
        void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to);
        void OnGoalLoad(AIGoal goal);
        bool TryGetGoal(out AIGoal goal);
        bool TryClearGoal(string reason = null);
        bool HasGoal { get; }
    }
}
