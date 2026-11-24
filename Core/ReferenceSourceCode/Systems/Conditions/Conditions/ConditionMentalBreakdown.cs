using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionMentalBreakdown : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionMentalBreakdown());
        }
        public override Priority Importance => Priority.Third;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
        }

        private ConditionMentalBreakdown() : base(ConditionId.MentalBreakdown, IconId.CStress) {
            nameTFunc = () => T.NounMentalBreakdown;
            PreventsColonyWork = true;
            PreventsControl = true;
        }
    }
}
