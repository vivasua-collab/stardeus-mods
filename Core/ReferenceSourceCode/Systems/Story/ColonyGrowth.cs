using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    // The one definition of "does this colony want more people", the population
    // intent. StasisWakeUpStoryAspect owns the config and feeds it here at Initialize;
    // EventPickerStoryAspect only reads, so the wake-up schedule and the story-event weights
    // can never disagree about it. Static because aspect instances are shared by every game
    // in a session (StoryGenDef.LinkAspects), which is also why nothing here may outlive
    // Configure. See Docs/Architecture/Systems/StorySystem.md, "Colony growth".
    public static class ColonyGrowth {
        private const string K_LastChange = "Population.LastChange";
        private const string K_LastColonists = "Population.LastColonists";

        private static Curve intentCurve;
        private static Curve floorCurve;
        private static int holdTicks;

        public static void Configure(Curve intent, Curve floor, float minDaysBetweenGrowth) {
            intentCurve = intent;
            floorCurve = floor;
            holdTicks = (int) (minDaysBetweenGrowth * Consts.TicksPerDay);
        }

        // Population -> intent, 0 at the ceiling. Population counts bots as people
        // (Situation.Population): they do a colonist's work and the same storyteller grows
        // them, so a bot fleet is a big colony even with two humans aboard.
        public static float Intent(StoryContext ctx) {
            if (intentCurve == null) { return 1f; }
            return Mathf.Max(0f, intentCurve.Evaluate(ctx.S.Situation.Population));
        }

        // Biological growth cannot be starved by a bot fleet while the crew is still small.
        // The floor reads colonists alone and only matters when it is above intent.
        public static float BioIntent(StoryContext ctx) {
            var intent = Intent(ctx);
            if (floorCurve == null) { return intent; }
            return Mathf.Max(intent, floorCurve.Evaluate(ctx.S.Situation.Colonists));
        }

        // No biological growth of any kind before this tick. Installed by any change in
        // colonist count, up or down - population adaptation - so a joiner does
        // not land on the heels of a wake-up and a death is not replaced the same day.
        public static long HoldUntilTick(StoryContext ctx) {
            // Never stamped (a new game, or a save from before this existed): no hold at
            // all. Reading the missing key as tick 0 would put a hold over the opening of
            // every new game, which is the opening grace's job and its number, not this one's.
            if (!ctx.AspectData.TryGetLong(K_LastChange, out var lastChange)) { return 0L; }
            return lastChange + holdTicks;
        }

        public static bool IsHolding(StoryContext ctx) {
            return ctx.Ticks < HoldUntilTick(ctx);
        }

        // Called hourly by the wake-up aspect. A missing key means the first tick of a new
        // game or of a save from before this existed: seed it, do not read the 0 -> N jump
        // as a change or every old save would load into a spurious hold.
        public static void TrackColonists(StoryContext ctx) {
            var colonists = ctx.S.Situation.Colonists;
            var data = ctx.AspectData;
            if (!data.TryGetInt(K_LastColonists, out var last)) {
                data.SetInt(K_LastColonists, colonists);
                return;
            }
            if (colonists == last) { return; }
            data.SetInt(K_LastColonists, colonists);
            data.SetLong(K_LastChange, ctx.Ticks);
        }
    }
}
