using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.AI.Combat {
    // What a humanoid mind adds to a fight beyond cover (CombatUnit.IsTactical):
    // badly hurt colonists retreat, and a human nearby makes friendly bots shoot straighter.
    public sealed partial class CombatUnit {
        /// <summary>
        /// A badly hurt colonist (humanoid mind, not drafted) breaks off the fight: no target,
        /// no combat goals (AISys), no shots, and the healing need takes it to a bed. Derived
        /// from vitality every tick, so nothing is saved. Bots fight until they drop.
        /// </summary>
        public bool IsRetreating { get; private set; }

        /// <summary>
        /// A non-tactical unit (a bot) with a human of its own army close by and in sight:
        /// it aims better (AttackMethod.HitChance). Rechecked every few ticks.
        /// </summary>
        public bool IsCommanded { get; private set; }

        private const int CommandCheckInterval = 10;
        // spreads the checks of different units over the interval
        private readonly int commandCheckPhase = Rng.URange(0, CommandCheckInterval);
        #if !PRODUCTION_BUILD
        public static int DebugCommandChecks;
        #endif

        private void UpdateRetreat(long ticks) {
            if (!CanRetreat()) {
                IsRetreating = false;
                return;
            }
            var vitality = Data.Body.VitalityPercent;
            if (IsRetreating) {
                if (vitality >= Tunables.CombatRetreatVitality
                        + Tunables.CombatRetreatRecoverMargin) {
                    IsRetreating = false;
                }
                return;
            }
            if (vitality >= Tunables.CombatRetreatVitality) { return; }
            IsRetreating = true;
            if (AttackTarget != null) {
                SetAttackTarget(null, force: true);
            }
            CurrentAttack = null;
            if (Agent.Goal?.Type == AIGoalType.Combat) {
                AIGoal.Cancel(Agent.Goal, ticks, T.CombatStateEscape);
            }
        }

        private bool CanRetreat() {
            if (!Data.IsPlayer || !IsTactical) { return false; }
            // a drafted unit stays where the player put it
            if (Agent.DirectControl.IsUnderDirectControl) { return false; }
            if (!Data.Body.IsAlive || Data.Body.IsIncapacitated) { return false; }
            var army = Army;
            if (army == null) { return false; }
            // fistfights are not worth running from
            return (army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly;
        }

        // Hot path (every unit, every tick): returns at once except on its own phase, and then
        // scans only its own army, distance first, with one sight check for the nearest human.
        private void UpdateCommand(long ticks) {
            if (ticks % CommandCheckInterval != commandCheckPhase) { return; }
            IsCommanded = false;
            if (IsTactical) { return; }
            var army = Army;
            if (army == null) { return; }
            #if !PRODUCTION_BUILD
            DebugCommandChecks++;
            #endif
            var radius = Tunables.CombatCommandRadius;
            var bestSqr = radius * radius;
            CombatUnit nearest = null;
            var units = army.Units;
            for (int i = units.Count - 1; i >= 0; i--) {
                var u = units[i];
                if (u == this || !u.IsTactical || u.IsFleeing || u.IsRetreating) { continue; }
                if (u.State == CombatUnitState.Incapacitated || !u.Data.Body.IsAlive) { continue; }
                var sqr = Vectors.SqrDistance(u.Position, Position);
                if (sqr < bestSqr) {
                    bestSqr = sqr;
                    nearest = u;
                }
            }
            if (nearest == null) { return; }
            IsCommanded = EntityUtils.HasLineOfSightCoverFast(SightPosition,
                nearest.SightPosition, 0f, radius + 1f);
        }
    }
}
