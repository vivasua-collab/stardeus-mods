using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Notify {
    // A colonist going down used to leave only a line in their own log, and a playtester lost
    // their only colonist that way without noticing. A clickable notification plus a fanfare
    // makes it impossible to miss; the Body removes the notification when they recover.
    //
    // Biological colonists only: bots go down every time their charge runs out and come back
    // when it trickles in, and an alert for each of those flips drowned everything else.
    // The cooldown covers a colonist whose state flips the same way (e.g. bleeding at the
    // edge of consciousness).
    public static class IncapacitationAlert {
        private const long CooldownTicks = Consts.TicksPerHour * 6;

        public static void Show(GameState s, BeingData being, string reason,
                ref NotificationHandle handle, ref long lastAlertTick) {
            if (being == null || !being.IsPlayer || being.IsGuest) { return; }
            if (!being.Species.IsBiological) { return; }
            if (handle.IsValid) { return; }
            var now = G.Ticks;
            if (lastAlertTick > 0 && now - lastAlertTick < CooldownTicks) { return; }
            lastAlertTick = now;
            var name = Texts.LinkToBeing(being);
            var text = string.IsNullOrEmpty(reason)
                ? $"{name}: {T.BeingStateIncapacitated}"
                : $"{name}: {reason}";
            handle = Notification.BuildFor(being, null, text, Consts.TicksPerDay,
                    NotificationType.Alert)
                .Group("incapacitated", title: T.BeingStateIncapacitated,
                    icon: IconId.CWarning)
                .Show();
            s.Sig.ShowFanfare.Send(Texts.Red(text));
        }

        public static void Clear(ref NotificationHandle handle) {
            Notification.Remove(ref handle);
        }
    }
}
