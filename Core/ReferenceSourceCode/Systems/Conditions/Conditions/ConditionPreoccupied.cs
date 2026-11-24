using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionPreoccupied : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionPreoccupied());
        }
        public override Priority Importance => Priority.None;

        // Belongs to a fistfight and ends with it. Heals saves where a fight ended without
        // taking it away (STA-1750)
        public override void Tick(GameState s, ConditionState cond, long ticks) {
            var army = cond.Owner.Being?.AIAgent?.CombatUnit?.Army;
            if (army == null || (army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly) {
                cond.Owner.RemoveCondition(s, IdH);
            }
        }

        private ConditionPreoccupied() : base(ConditionId.Preoccupied, IconId.CStress) {
            PreventsColonyWork = true;
            PreventsControl = true;
        }
    }
}
