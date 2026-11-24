namespace Game.Systems.Policies {
    // What to do with an already equipped item that the policy no longer allows.
    public enum IncompatibleAction : byte {
        // Wear it until something the policy allows shows up.
        KeepUntilReplaced = 0,
        // Take it off and drop it, even when there is no replacement.
        UnequipNow = 1,
    }
}
