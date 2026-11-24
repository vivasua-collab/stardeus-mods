using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionHypoxia : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionHypoxia());
        }
        public override Priority Importance => Priority.Critical;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (cond.Severity <= Severity.Mild) { return; }
            var expSev = cond.Severity - 1;
            var source = NamedEntity.For(NameT);
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                if (slot.Type.IdH == BodyPartIdH.Brain
                        || slot.Type.IdH == BodyPartIdH.Heart
                        || slot.Type.IdH == BodyPartIdH.Lung) {
                    slot.TryAddOrUpdateWound(source,
                        DamageType.Organ, expSev, ticks, 1f, conditionId: IdH);
                }
            }
        }

        private ConditionHypoxia() : base(ConditionId.Hypoxia, IconId.CLowO2) {}
    }
}
