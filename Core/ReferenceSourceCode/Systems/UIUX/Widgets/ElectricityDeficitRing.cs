namespace Game.Systems.UIUX {
    // Decides how the top bar electricity widget rings when the grid falls into deficit. The
    // widget only turned red and the deficit alert sat in the hub, and both went unnoticed, so
    // a fresh deficit gets the focus ring that points at the widget from across the screen.
    // A grid running at its limit dips in and out of deficit as devices cycle, and a
    // screen-wide sweep on every dip would bury the game, so a deficit that comes back before
    // the grid stayed clear for ResolvedAfter only pulses. Real time, like
    // NotificationHubNeglect: at the fastest clock speed a game hour passes in seconds, and a
    // game-time window would sweep the screen every few seconds there.
    public sealed class ElectricityDeficitRing {
        public const double ResolvedAfter = 60;
        private const double NotSet = double.MinValue;

        public enum Kind {
            None,
            Focus,
            Pulse,
        }

        private bool inDeficit;
        private double clearSince = NotSet;

        // Called on every situation update; only the start of a deficit rings
        public Kind Next(bool hasDeficit, double now) {
            if (!hasDeficit) {
                if (inDeficit) {
                    inDeficit = false;
                    clearSince = now;
                }
                return Kind.None;
            }
            if (inDeficit) { return Kind.None; }
            inDeficit = true;
            if (clearSince == NotSet || now - clearSince >= ResolvedAfter) {
                return Kind.Focus;
            }
            return Kind.Pulse;
        }
    }
}
