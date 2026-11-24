#if UNITY_EDITOR
// #define DEBUG_WAKEUP
#endif
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Mods;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    // The colony's primary biological growth path, on a schedule the player can rely on but
    // not predict: an interval derived from population intent (see ColonyGrowth), with a
    // jitter roll per wake-up. Story-event joiners are a bounded extra on top - they cannot
    // move this clock, and after every wake-up the ColonyGrowthBio cooldown is stamped to at
    // least one interval so they can never outnumber wake-ups.
    public sealed class StasisWakeUpStoryAspect : StoryAspect, ISaveableChild {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new StasisWakeUpStoryAspect();
        }

        private Curve intentCurve;
        private Curve floorCurve;
        private float baseGrowthDays;
        private float growthJitter;
        // Rolled once per wake-up, so the interval is stable between wake-ups and only
        // population (through intent) or the hold can move the date the player is waiting for.
        private float jitterMul = 1f;
        private StoryEventDef wakeUpEventDef;
        private long lastWakeupTick;
        private long retryAfterTick;
        // What the schedule looked like before the wake-up currently being announced took
        // its slot. A warned wake-up can still be called off during its half day of notice,
        // and everything it spent has to go back where it came from, or the retry lands an
        // interval away instead of the next day - see RetryAnnouncedWakeUp. Saved, because
        // the notice can outlive a save.
        private long announcedFromTick;
        private float announcedFromJitter = 1f;
        private bool announcedFromHadWakeUp;
        // Whether this game has had its first wake-up yet. Until it has, and only while
        // nobody biological is aboard, the opening grace below is the date rather than a
        // floor - see NextWakeupTick.
        private bool hadWakeUp;
        // No wake-up before this tick, whatever the interval says. Buys the player a quiet
        // opening stretch to learn power and building in before they have to keep a person
        // alive. Comes from the aspect's InitialExtraDelayDays; 0 means no grace.
        private long earliestWakeupTick;
        // While the tutorial chain runs, the first wake-up on an empty ship waits for the
        // player to finish the stasis room rather than for the grace: both first-time
        // playtesters were still building it when the fixed day 6-8 wake-up came. This is the
        // latest it may wait, so a player who never takes the quest still gets a colonist.
        // 0 on saves from before it existed, which then fall back to earliestWakeupTick.
        private long firstWakeupCeilingTick;
        // The ceiling is this many graces after the start: the emergency-powered array keeps
        // the sleepers stable, so waiting longer costs the player nothing but time.
        private const float CeilingGraces = 2f;

        public const string AspectId = "StasisWakeUp";
        public override string Id => AspectId;

        private string DataKey(string key) => $"{Def.Id}.{key}";

        public static StasisWakeUpStoryAspect For(StoryContext ctx) {
            foreach (var asp in ctx.S.Query.GetStoryGen.Ask().StoryAspects) {
                if (asp is StasisWakeUpStoryAspect wake) { return wake; }
            }
            return null;
        }

        public void OnSave(ComponentData data) {
            data.SetLong(DataKey("lastWakeup"), lastWakeupTick);
            data.SetLong(DataKey("announcedFrom"), announcedFromTick);
            data.SetFloat(DataKey("announcedJitter"), announcedFromJitter);
            data.SetBool(DataKey("announcedHadWakeup"), announcedFromHadWakeUp);
            data.SetLong(DataKey("earliestWakeup"), earliestWakeupTick);
            data.SetFloat(DataKey("jitter"), jitterMul);
            data.SetBool(DataKey("hadWakeup"), hadWakeUp);
            data.SetLong(DataKey("firstWakeupCeiling"), firstWakeupCeilingTick);
        }

        public void OnLoad(ComponentData data) {
            // StorySys calls this on a new game too, with empty aspect data, right after
            // Initialize. A missing key therefore has to keep what Initialize computed, not
            // zero it - zeroing earliestWakeupTick silently dropped the whole grace period.
            lastWakeupTick = data.GetLong(DataKey("lastWakeup"), lastWakeupTick);
            announcedFromTick = data.GetLong(DataKey("announcedFrom"), announcedFromTick);
            announcedFromJitter = data.GetFloat(DataKey("announcedJitter"), announcedFromJitter);
            announcedFromHadWakeUp = data.GetBool(DataKey("announcedHadWakeup"),
                announcedFromHadWakeUp);
            earliestWakeupTick = data.GetLong(DataKey("earliestWakeup"), earliestWakeupTick);
            jitterMul = data.GetFloat(DataKey("jitter"), jitterMul);
            hadWakeUp = data.GetBool(DataKey("hadWakeup"), hadWakeUp);
            firstWakeupCeilingTick = data.GetLong(DataKey("firstWakeupCeiling"),
                firstWakeupCeilingTick);
        }

        // long.MaxValue when population is at the ceiling: no wake-up until it drops.
        public long NextWakeupTick(StoryContext ctx) {
            if (lastWakeupTick <= 0L) {
                lastWakeupTick = ctx.Ticks;
            }
            var bioIntent = ColonyGrowth.BioIntent(ctx);
            if (bioIntent <= 0f) {
                return long.MaxValue;
            }
            // Whichever is latest wins, so the dev console's "next wakeup in" and the tick
            // gate below read the same number during the grace period and the hold
            var hold = ColonyGrowth.HoldUntilTick(ctx);
            // For a colony with nobody biological aboard yet, the opening grace IS the first
            // wake-up's date rather than a floor under an interval that is longer than it
            // anyway: there is no crew for population to space the arrival against, and the
            // ship needs its first pair of hands as soon as there is a room to put them in.
            // New Beginning and Cargo Ship start this way; without it their opening
            // population of 3 bots already asked for the second colonist's gap
            // (20 / 1.4 = 14 days) and jitter pushed the first wake-up out to day 18.
            //
            // Scenarios that start with crew (Wrecked, No Robots, Leaving Earth) keep the
            // normal interval, so their first wake-up stays a proper population-paced gap
            // and the grace is only a floor. Read live rather than captured at Initialize,
            // so an empty ship that gains a colonist some other way before its first
            // wake-up loses the shortcut too - it has hands now.
            if (!hadWakeUp && ctx.S.Situation.Colonists == 0) {
                return FirstWakeupTick(ctx, hold);
            }
            var days = baseGrowthDays / bioIntent * jitterMul;
            var pace = ModifierStoryPace.Current;
            if (pace <= 0f) { pace = 1f; }
            days /= pace;
            var next = lastWakeupTick + (long) (days * Consts.TicksPerDay);
            if (hold > next) { next = hold; }
            return next > earliestWakeupTick ? next : earliestWakeupTick;
        }

        // The first wake-up on a ship with nobody biological aboard. With the tutorial chain
        // running it is due the moment the stasis room is survivable (the colonist then gets
        // the usual half day of notice, during which the player is building the bed the
        // chain now asks for), and at the ceiling at the latest. Scenarios without the chain
        // (custom starts) never set the bool, so they keep the plain grace.
        private long FirstWakeupTick(StoryContext ctx, long hold) {
            var at = earliestWakeupTick;
            var q = ctx.S.Query;
            if (firstWakeupCeilingTick > 0
                    && q.GetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests)) {
                at = q.GetQuestBool.Ask(QuestBoolIds.MadeStasisSurvivable)
                    ? 0L
                    : firstWakeupCeilingTick;
            }
            return hold > at ? hold : at;
        }

        #if !PRODUCTION_BUILD
        public long LastWakeupTick {
            get => lastWakeupTick;
            set => lastWakeupTick = value;
        }
        #endif

        public override void OnTick(StoryContext ctx) {
            if (!ctx.IsEveryHour) { return; }
            // Ahead of the retry gate: a day-long retry window must not swallow a change
            ColonyGrowth.TrackColonists(ctx);
            if (ctx.Ticks < retryAfterTick) { return; }
            if (ctx.Ticks < NextWakeupTick(ctx)) {
                #if DEBUG_WAKEUP
                D.Warn("Last wake up: {0}", Units.TicksAgo((int) (G.Ticks - lastWakeupTick)));
                D.Warn("Next wake up in {0}", Units.TicksToTime(NextWakeupTick(ctx) - G.Ticks));
                #endif
                return;
            }

            if (!wakeUpEventDef.Event.CheckConditions(ctx, wakeUpEventDef)) {
                retryAfterTick = ctx.Ticks + Consts.TicksPerDay;
                return;
            }

            var data = StoryEventData.CreateFor(wakeUpEventDef);
            data.Seed = ctx.Rng.Int;
            // The scheduled wake-up is the only one that gives notice, and the only one the
            // player did not ask for. The snapshot is taken before the slot below is spent,
            // so an event that fails at start rolls back to what it already has.
            announcedFromTick = lastWakeupTick;
            announcedFromJitter = jitterMul;
            announcedFromHadWakeUp = hadWakeUp;
            StasisWakeUpWarning.Arm(data);
            ctx.StartEvent(data);
            if (data.IsFailed) {
                retryAfterTick = ctx.Ticks + Consts.TicksPerDay;
            } else {
                lastWakeupTick = ctx.Ticks;
                hadWakeUp = true;
                RollJitter(ctx);
                StampJoinerCooldown(ctx);
            }
        }

        // An announced wake-up that never happened: give the interval back, then hold off
        // for a day like any other failed attempt, so the colony is asked again soon rather
        // than waiting out a full interval for a wake-up it never got.
        public void RetryAnnouncedWakeUp(StoryContext ctx) {
            lastWakeupTick = announcedFromTick;
            jitterMul = announcedFromJitter;
            hadWakeUp = announcedFromHadWakeUp;
            // The joiner cooldown stamped when the notice went out is left where it is: it
            // only holds back a competing story joiner, and it runs out around the time this
            // retry lands anyway.
            retryAfterTick = ctx.Ticks + Consts.TicksPerDay;
        }

        private void RollJitter(StoryContext ctx) {
            jitterMul = ctx.Rng.Range(1f - growthJitter, 1f + growthJitter);
        }

        // Story-event joiners wait at least until the next wake-up is due, on top of whatever
        // the ColonyGrowthBio cooldown def already says. Worst case is strict alternation,
        // so stasis never provides less than half of biological growth. FlagCooldowns is
        // written directly because CoolDownFlag would overwrite a longer cooldown.
        private void StampJoinerCooldown(StoryContext ctx) {
            var until = NextWakeupTick(ctx);
            if (until == long.MaxValue) { return; }
            if (!ctx.FlagCooldowns.TryGetValue(StoryEventFlags.ColonyGrowthBio, out var existing)
                    || existing < until) {
                ctx.FlagCooldowns[StoryEventFlags.ColonyGrowthBio] = until;
            }
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            var eventId = Def.Vars.Get(PropertyIdH.StoryEventId, string.Empty);
            if (string.IsNullOrEmpty(eventId)) {
                D.Err("StasisWakeUpStoryAspect has no StoryEventId property: {0}", Def);
                eventId = "OnDemand/StasisWakeUp";
            }
            wakeUpEventDef = StoryEventDef.Get(eventId);
            if (wakeUpEventDef == null) {
                var msg = string.Format(
                    "StasisWakeUpStoryAspect: found no StoryEventDef by id: {0} ({1})",
                    eventId, Def);
                ModRegistry.WarnAboutBrokenMod(Def, msg);
                D.Err(msg);
                return;
            }

            LoadCurve(PropertyIdH.PopulationIntentCurve, ref intentCurve);
            LoadCurve(PropertyIdH.ColonistFloorCurve, ref floorCurve);
            baseGrowthDays = Def.Vars.Get(PropertyIdH.BaseGrowthDays, 20f);
            growthJitter = Mathf.Clamp(Def.Vars.Get(PropertyIdH.GrowthJitter, 0.3f), 0f, 0.9f);
            // Aspect instances are shared by every game played in a session
            // (StoryGenDef.LinkAspects), so nothing may carry over from the previous one -
            // including what ColonyGrowth holds
            ColonyGrowth.Configure(intentCurve, floorCurve,
                Def.Vars.Get(PropertyIdH.MinDaysBetweenGrowth, 10f));
            retryAfterTick = 0L;
            if (!isLoading) {
                lastWakeupTick = ctx.Ticks;
                var graceDays = Def.Vars.Get(PropertyIdH.InitialExtraDelayDays, 0f);
                // Rolled once per game, so on a ship that starts empty the first colonist
                // does not arrive on a day the player can count to from the loading screen.
                // Deliberately much tighter than GrowthJitter: the opening is a hand-placed
                // beat sized to outlast the stasis-area quest chain, so it may drift a day
                // either way but must not land inside the first week or slip well past it.
                var graceJitter = Mathf.Clamp(
                    Def.Vars.Get(PropertyIdH.InitialExtraDelayJitter, 0f), 0f, 0.9f);
                graceDays *= ctx.Rng.Range(1f - graceJitter, 1f + graceJitter);
                earliestWakeupTick = ctx.Ticks
                    + (long) (graceDays * Consts.TicksPerDay);
                firstWakeupCeilingTick = graceDays > 0f
                    ? ctx.Ticks + (long) (graceDays * CeilingGraces * Consts.TicksPerDay)
                    : 0L;
                hadWakeUp = false;
                RollJitter(ctx);
                // Only ever read for a wake-up that was announced and then called off, so
                // until the first one goes out these are just today's numbers.
                announcedFromTick = lastWakeupTick;
                announcedFromJitter = jitterMul;
                announcedFromHadWakeUp = false;
            } else {
                // OnLoad restores all of these next. A save without the keys gets no grace, a
                // fresh lastWakeupTick and no jitter, not whatever the last game left behind.
                lastWakeupTick = 0L;
                announcedFromTick = 0L;
                announcedFromJitter = 1f;
                announcedFromHadWakeUp = true;
                earliestWakeupTick = 0L;
                firstWakeupCeilingTick = 0L;
                jitterMul = 1f;
                // True, not false, for a save from before this key existed: its opening is
                // long past, and its earliestWakeupTick loads as 0, so treating it as still
                // waiting for the first wake-up would fire one immediately.
                hadWakeUp = true;
            }
        }

        public override void Unload(StoryContext ctx) {
        }
    }
}
