using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.AI.Combat {
    // A drafted unit's attack order, decided when the player gives it. An order on a target
    // the shooter can't hit from where it stands is refused for one selected shooter: the
    // player places it. Shift (or a group order, where placing each unit by hand would be
    // tedious) walks shooters into range instead.
    // Melee units always go after their target.
    public static class CombatOrders {
        public enum Kind {
            // a melee unit, or a shooter told to close in: walks after the target
            Approach,
            // a shooter that can hit the target from where it stands: fires, never moves
            Hold,
            // a shooter that can't hit the target from here and was not told to close in
            RefusedOutOfRange,
            RefusedNoSight,
        }

        public static Kind KindFor(CombatUnit cu, CombatUnit target, bool mayApproach) {
            var attack = cu.PreviewBestAttack();
            if (attack == null || attack.IsMelee) {
                return Kind.Approach;
            }
            var dist = Vectors.Distance(cu.Position, target.Position);
            // closer than the gun's minimum: the punch answers from here
            if (dist < attack.Stats.RangeMin.Value) {
                return Kind.Hold;
            }
            if (dist > attack.Stats.RangeMax.Value) {
                return mayApproach ? Kind.Approach : Kind.RefusedOutOfRange;
            }
            if (!EntityUtils.HasLineOfSightCover(cu.SightPosition, target.SightPosition, 0.5f,
                    CombatUnit.SightRange, out _)) {
                return mayApproach ? Kind.Approach : Kind.RefusedNoSight;
            }
            return Kind.Hold;
        }

        public static bool IsRefused(Kind kind) {
            return kind == Kind.RefusedOutOfRange || kind == Kind.RefusedNoSight;
        }

        public static string RefusalReason(Kind kind) {
            return kind == Kind.RefusedNoSight ? T.CombatShotNoSight : T.CombatShotOutOfRange;
        }

        // After the target was assigned (FreeSelectCombatHandler.SetAttackTarget)
        public static void Apply(CombatUnit cu, Kind kind) {
            cu.OrderMayApproach = kind == Kind.Approach;
        }
    }
}
