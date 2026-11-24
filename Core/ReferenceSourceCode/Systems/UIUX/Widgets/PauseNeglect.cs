namespace Game.Systems.UIUX {
    // Tells when a player has not noticed that time is paused, so the focus ring can point at
    // the pause button. A playtester new to the genre paused, ordered a connector, and then
    // cancelled, re-placed and re-prioritized it for minutes, waiting for a worker who could
    // never come. The pause frame and the lit pause button meant nothing to them: a colored
    // frame is a symbol that has to be learned first.
    // Two things give it away. Inspecting an order placed during this pause: nothing can have
    // happened to it yet, so the player is looking for why. And still clicking around the world
    // WaitingAfter since the last order placed during this pause: a player who plans while
    // paused on purpose resumes once the plan is placed. Either one asks for the ring at most
    // once per FocusCooldown, across pauses, so a veteran who plans while paused sees it seldom.
    // A pause is keyed by the tick it froze on. Ticks do not advance while paused, so a new tick
    // means time ran and the orders are being worked on. Real time, like NotificationHubNeglect:
    // attention is spent in real time, and a paused game has no other clock.
    public sealed class PauseNeglect {
        public const double WaitingAfter = 30;
        public const double FocusCooldown = 60;
        private const double NotSet = double.MinValue;

        private long pauseTick = long.MinValue;
        private double lastOrder = NotSet;
        private double lastFocus = NotSet;

        // A player order placed while time is paused on `tick`
        public void OrderPlaced(long tick, double now) {
            pauseTick = tick;
            lastOrder = now;
        }

        // The player selected something whose order was placed during this pause
        public bool ShouldFocusOnInspect(double now) {
            return TrySpendCooldown(now);
        }

        // A click in the world while time is paused on `tick`
        public bool ShouldFocusOnClick(long tick, double now) {
            if (lastOrder == NotSet || tick != pauseTick) { return false; }
            if (now - lastOrder < WaitingAfter) { return false; }
            return TrySpendCooldown(now);
        }

        private bool TrySpendCooldown(double now) {
            if (lastFocus != NotSet && now - lastFocus < FocusCooldown) { return false; }
            lastFocus = now;
            return true;
        }
    }
}
