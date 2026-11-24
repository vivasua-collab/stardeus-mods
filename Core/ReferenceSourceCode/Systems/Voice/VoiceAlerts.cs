namespace Game.Systems.Voice {
    /// <summary>
    /// Entry point for requesting a spoken event callout. Actual playback, throttling
    /// and de-duplication are handled by VoiceSys.
    /// </summary>
    public static class VoiceAlerts {
        public static void Say(string lineId) {
            if (string.IsNullOrEmpty(lineId)) { return; }
            A.S.Sig.SayVoiceLine.Send(lineId);
        }
    }
}
