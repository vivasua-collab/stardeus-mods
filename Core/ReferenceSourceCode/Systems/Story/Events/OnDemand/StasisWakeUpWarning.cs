using Game.Constants;
using Game.Systems.Notify;
using Game.Utils;

namespace Game.Systems.Story {
    // A scheduled wake-up announces itself half a day before it happens. Coming out of
    // stasis used to be the one colony growth event with no lead time: the choice screen
    // opened on whatever the colony happened to be at that moment - possibly a ship with no
    // room, no bed and no air for the person in it - and knowing about it earlier was the
    // only thing the player was missing. The warning is the whole reason this event is long
    // running rather than instant.
    //
    // Only StasisWakeUpStoryAspect arms it. A wake-up that somebody asked for on purpose
    // (the array's own wake-up request, the dev button, a mod) already had its lead time and
    // still happens at once.
    public static class StasisWakeUpWarning {
        public const int WarnTicks = Consts.TicksPer12Hours;
        private const string ArmedKey = "Warn";

        public static void Arm(StoryEventData data) {
            data.GetOrCreateData().SetBool(ArmedKey, true);
        }

        public static bool IsArmed(StoryEventData data) {
            return data.Data?.GetBool(ArmedKey, false) == true;
        }

        public static bool IsPending(StoryContext ctx, StoryEventData data) {
            return IsArmed(data) && ctx.Ticks < data.StartTick + WarnTicks;
        }

        public static void Announce(StoryContext ctx, StoryEventData data) {
            ctx.S.Sig.ShowFanfare.Send(Texts.Yellow(T.StasisWakeupSoonTitle));
            ShowNotification(data);
        }

        // Notification handles are not saved, so a game saved inside the warning window
        // loads without the hub entry and the event puts it back on its next tick. Sticky,
        // because an entry the player could close would come straight back from that same
        // tick - and there is nothing to dismiss, the warning removes itself when the
        // wake-up happens. No icon: the array's own sprite, because the Cryogenics icon is a
        // triangle and reads as a generic warning in the Warning group.
        public static void ShowNotification(StoryEventData data) {
            if (data.Event.IsValid || data.TargetTile == null) { return; }
            data.Event = Notification.BuildFor(data.TargetTile, null,
                    T.StasisWakeupSoonTitle, 0, NotificationType.Warning)
                .Sticky()
                .WithTooltip(T.StasisWakeupSoonDesc)
                .Show();
        }
    }
}
