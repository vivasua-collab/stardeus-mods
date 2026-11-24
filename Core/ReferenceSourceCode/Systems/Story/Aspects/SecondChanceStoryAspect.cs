using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.Notify;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    // The colony's safety net. Stardeus only ends a game when the ship computer is destroyed,
    // so losing the whole crew is not a loss - it is a ship that keeps ticking with nobody left
    // to do anything, forever. When that happens the ship AI transmits a distress beacon, and a
    // stasis pod with a rescue crew lands a day later. Once per RescueCooldownDays, so it stays
    // a rescue rather than a labor supply.
    public sealed class SecondChanceStoryAspect : StoryAspect, ISaveableChild {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new SecondChanceStoryAspect();
        }

        public const string AspectId = "SecondChance";
        public override string Id => AspectId;

        private StoryEventDef rescueEventDef;
        private long delayTicks;
        private long cooldownTicks;
        // 0 = no beacon out. The rescue is timed from here rather than from the moment the
        // colony went quiet, so the player always gets the full warning even when the cooldown
        // happens to expire in the middle of a crisis.
        private long beaconSentTick;
        // 0 = never rescued.
        private long lastRescueTick;

        private string DataKey(string key) => $"{Def.Id}.{key}";

        public void OnSave(ComponentData data) {
            data.SetLong(DataKey("beaconSent"), beaconSentTick);
            data.SetLong(DataKey("lastRescue"), lastRescueTick);
        }

        public void OnLoad(ComponentData data) {
            // StorySys calls this on a new game too, with empty aspect data, right after
            // Initialize - so a missing key has to keep what Initialize set, not zero it.
            beaconSentTick = data.GetLong(DataKey("beaconSent"), beaconSentTick);
            lastRescueTick = data.GetLong(DataKey("lastRescue"), lastRescueTick);
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            var eventId = Def.Vars.Get(PropertyIdH.StoryEventId, string.Empty);
            if (string.IsNullOrEmpty(eventId)) {
                D.Err("SecondChanceStoryAspect has no StoryEventId property: {0}", Def);
                eventId = "OnDemand/SecondChancePod";
            }
            rescueEventDef = StoryEventDef.Get(eventId);
            if (rescueEventDef == null) {
                var msg = string.Format(
                    "SecondChanceStoryAspect: found no StoryEventDef by id: {0} ({1})",
                    eventId, Def);
                ModRegistry.WarnAboutBrokenMod(Def, msg);
                D.Err(msg);
                return;
            }
            delayTicks = (long) (Def.Vars.Get(PropertyIdH.RescueDelayHours, 24f)
                * Consts.TicksPerHour);
            cooldownTicks = (long) (Def.Vars.Get(PropertyIdH.RescueCooldownDays, 100f)
                * Consts.TicksPerDay);
            // Aspect instances are shared by every game played in a session
            // (StoryGenDef.LinkAspects), so nothing may carry over from the previous one.
            // OnLoad restores both of these next when a save has them.
            beaconSentTick = 0L;
            lastRescueTick = 0L;
        }

        public override void OnTick(StoryContext ctx) {
            if (!ctx.IsEveryHour) { return; }
            if (rescueEventDef == null) { return; }
            // Read live, never cached: instant build can be switched off mid-game, and the
            // watchdog has to start working from that moment. It is a compile time false in a
            // production build, so a shipped game always has the net.
            if (A.InstantBuild) { return; }
            var s = ctx.S;

            if (!ColonyLifeSigns.IsColonyHelpless(s)) {
                if (beaconSentTick != 0L) {
                    StandDownBeacon(s);
                }
                return;
            }

            if (lastRescueTick != 0L && ctx.Ticks - lastRescueTick < cooldownTicks) {
                // Also the quiet window right after a drop, while the pod is still falling
                // and the colony is technically still helpless.
                return;
            }

            if (beaconSentTick == 0L) {
                SendBeacon(s, ctx.Ticks);
                return;
            }

            if (ctx.Ticks - beaconSentTick < delayTicks) { return; }
            // Dropping a rescue crew into a boarding action wastes it. With the crew gone
            // hostile combat ends on its own, so this cannot hold the rescue off forever.
            if (s.Sys.Combat.IsHostileCombatInProgress) { return; }
            if (!rescueEventDef.Event.CheckConditions(ctx, rescueEventDef)) { return; }

            var data = StoryEventData.CreateFor(rescueEventDef);
            data.Seed = ctx.Rng.Int;
            ctx.StartEvent(data);
            if (data.IsFailed) {
                // Retry next hour: the pod could not find a place to land, or the ship is in
                // hyperspace. The beacon stays out.
                return;
            }
            lastRescueTick = ctx.Ticks;
            beaconSentTick = 0L;
        }

        private void SendBeacon(GameState s, long ticks) {
            beaconSentTick = ticks;
            Notification.Show(IconId.CWarning, T.RescueBeaconSent,
                Consts.TicksPerDay, NotificationType.Warning);
            s.Sys.Log.AddLine(Texts.Yellow(T.RescueBeaconSent));
        }

        private void StandDownBeacon(GameState s) {
            beaconSentTick = 0L;
            s.Sys.Log.AddLine(Texts.Green(T.RescueBeaconStanddown));
        }

        public override void Unload(StoryContext ctx) {
        }
    }
}
