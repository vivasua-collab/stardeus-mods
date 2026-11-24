namespace Game.Systems.AI {
    // Implemented by IAIGoalProvider components that can opt a specific goal out of
    // its fail icon and fail-reason tooltip/popup text (e.g. a crafting order muted
    // via CraftingOrder.SuppressDeficitWarning).
    public interface IAISuppressableGoalProvider {
        bool ShouldSuppressGoalFailure(AIGoal goal);
    }
}
