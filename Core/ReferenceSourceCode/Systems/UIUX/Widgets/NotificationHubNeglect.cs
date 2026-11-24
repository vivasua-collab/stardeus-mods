namespace Game.Systems.UIUX {
    // Tells when the player has stopped looking at the notification hub. Playtesters kept
    // ignoring it despite the pulse ring on every new alert, so once the hub has gone
    // unopened for IgnoredAfter since an alert, new alerts get the stronger focus ring until
    // the player opens it. Only opening counts as looking: the hub emptying on its own does
    // not, since the player still never looked. Real time, not ticks: attention is spent in
    // real time, and a paused game with an unread alert is still an ignored hub.
    // A fight sends alerts for minutes, and a screen-wide ring after every one would bury
    // the game, so focus rings are FocusCooldown apart and alerts in between only pulse.
    public sealed class NotificationHubNeglect {
        public const double IgnoredAfter = 300;
        public const double FocusCooldown = 30;
        private const double NotSet = double.MinValue;

        private double unseenSince = NotSet;
        private double lastFocus = NotSet;

        public void MarkSeen() {
            unseenSince = NotSet;
            lastFocus = NotSet;
        }

        // Called for every alert that rings. The first one since the hub was last opened
        // starts the clock; later ones past IgnoredAfter ask for the focus ring, at most once
        // per FocusCooldown.
        public bool ShouldFocus(double now) {
            if (unseenSince == NotSet) {
                unseenSince = now;
                return false;
            }
            if (now - unseenSince < IgnoredAfter) { return false; }
            if (lastFocus != NotSet && now - lastFocus < FocusCooldown) { return false; }
            lastFocus = now;
            return true;
        }

        // For an alert that always gets the focus ring (Action Required). It still starts the
        // clock like any unseen alert, and spends the cooldown so an escalated alert right
        // after it does not sweep the screen a second time.
        public void MarkFocused(double now) {
            if (unseenSince == NotSet) {
                unseenSince = now;
            }
            lastFocus = now;
        }

        public void DebugAge(double seconds) {
            if (unseenSince != NotSet) {
                unseenSince -= seconds;
            }
            if (lastFocus != NotSet) {
                lastFocus -= seconds;
            }
        }
    }
}
