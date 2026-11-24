namespace Game.Systems.Mental {
    /// <summary>
    /// Mental breakdown severity band a being is currently in, decided by comparing its
    /// Sanity against the per-being breakdown threshold stats. Drives both the natural
    /// trigger chance (MTB per band) and which breakdown events are eligible.
    /// </summary>
    public enum MentalBreakdownBand {
        None,
        Light,
        Medium,
        Heavy,
    }
}
