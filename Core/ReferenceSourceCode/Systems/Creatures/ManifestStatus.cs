namespace Game.Systems.Creatures {
    // One field rather than two booleans: "never woken" (what the entity panel hides and
    // which wake-up path runs) and "came from a manifest at all" (what the ending counts)
    // are the same fact at different times. Default None (0), so old saves load unchanged.
    public enum ManifestStatus : byte {
        None = 0,
        // On a manifest and never woken: the player knows who they are, not what they can do.
        Sleeping = 1,
        // Came from a manifest and has been woken at least once. Stays Woken if later sent
        // back into stasis - that person is no longer a stranger.
        Woken = 2,
    }
}
