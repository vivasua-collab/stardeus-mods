using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionHyperthermia : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionHyperthermia());
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
            if (ticks % Consts.TicksPerMinute != 0) { return; }
            if (cond.Severity <= Severity.Mild) { return; }
            var expSev = cond.Severity - 1;
            var source = NamedEntity.For(NameT);
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                if (slot.Type.IdH == BodyPartIdH.Heart) {
                    slot.TryAddOrUpdateWound(source, DamageType.Organ, expSev,
                        ticks, 1f, conditionId: IdH);
                }
                if (!slot.IsInternal) {
                    if (Rng.UChance(0.02f * (int) expSev)) {
                        slot.TryAddOrUpdateWound(source, DamageType.Heat, expSev,
                            ticks, 1f, conditionId: IdH);
                    }
                }
            }
        }

        private ConditionHyperthermia() : base(ConditionId.Hyperthermia, IconId.CTemperature) {}
    }
}
