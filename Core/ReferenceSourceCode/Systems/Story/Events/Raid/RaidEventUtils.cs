using Game.CodeGen;
using Game.Data;
using Game.Systems.AI.Combat;

namespace Game.Systems.Story {
    /// <summary>
    /// Builds a raid story event's CombatRaid the same way everywhere: faction from the event
    /// data, and the def's RaidStrengthMul folded in through CombatRaid.AdjustStrength. Why a
    /// multiplier per event def: every raid event otherwise buys exactly one plan's worth of
    /// raiders, so the Large/Mega hullpiercer variants (50/150 story points, day 360/1000 gates)
    /// were only a regular raid spread thinner, and raids that arrive dispersed over several
    /// objects and minutes had no knob to compensate with. Going through AdjustStrength means the
    /// multiplier shows up as StrengthMul in RaidPlan.Describe and raid_report, no new formula.
    /// </summary>
    public static class RaidEventUtils {
        public static float StrengthMulFor(StoryEventDef def) {
            return def?.Vars.Get(PropertyIdH.RaidStrengthMul, 1f) ?? 1f;
        }

        public static CombatRaid NewRaidFor(GameState s, StoryEventData data, int seed) {
            data.Faction ??= s.Sys.Diplomacy.RandomFactionForRaid();
            var raid = new CombatRaid(s, seed) { Faction = data.Faction };
            raid.AdjustStrength(StrengthMulFor(data.Def));
            return raid;
        }
    }
}
