namespace Game.Systems.AI.Combat {
    public interface IAICombatAction {
        void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
            CombatUnit prevTarget, CombatUnit newTarget);
    }
}
