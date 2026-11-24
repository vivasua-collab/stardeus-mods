using Game.CodeGen;
using Game.Data;
using Game.Systems.Creatures;
using Unity.Mathematics;

namespace Game.Systems.AI.Combat {
    // Early mercy for a new colony: in the first raids, a hostile fighting several of the
    // crew at once aims worse for each one past the first (Raid/OutnumberedRaiderAccuracy,
    // keyed on the raids so far, gone from raid 6). A single well-geared raider out of the
    // first supply capsule took down a bot crew of three and ended a game in its first half
    // hour. Ganging up on a raider is what the player can do about it, and this makes it pay.
    //
    // It lowers the attacker's own accuracy (ShotOdds.Being), so aim, cover and the total
    // stay one product (StrayShot reads them) and a flustered raider's misses go wilder. Only
    // hostiles attacking the crew are touched: the crew's own odds, which the shot tooltip
    // shows, never change. Hostile fights that are not raids (animals, crawlers) count too
    // while the raid count is low; that is early mercy as well.
    public static class OutnumberedOdds {
        // a crowd past this does not make it worse
        private const int MaxStacked = 3;

        public static float AttackerMul(CombatUnit attacker, BeingData target) {
            if (target == null || !target.IsPlayer || attacker.Data.IsPlayer) { return 1f; }
            var army = attacker.Army;
            if (army == null || (army.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                return 1f;
            }
            var extra = attacker.EnemiesTargetingThisUnit.Count - 1;
            if (extra <= 0) { return 1f; }
            var perOpponent = CurveDef.FastGet(CurveIdH.Raid_OutnumberedRaiderAccuracy)
                .Evaluate(attacker.Agent.S.Situation.NumRaids);
            if (perOpponent >= 1f) { return 1f; }
            return math.pow(perOpponent, math.min(extra, MaxStacked));
        }
    }
}
