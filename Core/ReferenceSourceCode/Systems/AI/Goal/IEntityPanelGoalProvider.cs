namespace Game.Systems.AI {
    // Implemented by IAIGoalProvider components whose task changes what the entity panel
    // shows (a warning, a tab or its content). The panel of a selected entity reloads when
    // such a task comes, goes or is replaced. Every other task only refreshes the bottom bar:
    // a reload closes the open sub-panel and drops input focus, and a working crafter swaps
    // its haul task every batch (STA-1668).
    public interface IEntityPanelGoalProvider : IAIGoalProvider { }
}
