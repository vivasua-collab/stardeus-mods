namespace Game.Systems.Crafting {
    public enum CraftingDemandType {
        // Nothing will be built
        One,
        // Will use the custom count and build until crafter object produces
        // that much
        Custom,
        // Check all inventories and build if we don't have as much
        Limit,
        // If we have the resources, let's build it
        Unlimited,
    }
}
