namespace Game.Systems.Crafting {
    public enum CraftingOrderState : byte {
        InQueue,
        InProgress,
        ProducedEnough,
        Paused,
    }
}
