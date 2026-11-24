namespace Game.Systems.Policies {
    // Where an effective policy came from. Default and Global both present as
    // "Global" in the UI - Default just means the player never touched anything.
    public enum PolicyOrigin : byte {
        Default = 0,
        Global = 1,
        Species = 2,
        Individual = 3,
    }
}
