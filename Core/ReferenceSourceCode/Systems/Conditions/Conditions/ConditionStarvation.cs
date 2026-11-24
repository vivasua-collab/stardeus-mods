using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Mental;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionStarvation : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionStarvation());
        }
        public override Priority Importance => Priority.High;
        public override int MoodSourceH => MoodSourceIdH.Starving;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }
        public override void OnEnd(ConditionState cond, long ticks) {
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                if (slot.Type.IdH == BodyPartIdH.Stomach) {
                    // FIXME no need for flags anymore?
                    slot.TrySetWoundFlags(DamageType.Malnutrition, WoundFlags.None);
                } else if (slot.Type.IdH == BodyPartIdH.Intestines) {
                    slot.TrySetWoundFlags(DamageType.Malnutrition, WoundFlags.None);
                } else if (slot.IsEssential) {
                    slot.TrySetWoundFlags(DamageType.Malnutrition, WoundFlags.None);
                }
            }
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }
        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (ticks % Consts.TicksPer30Minutes == 0) {
                if (!cond.Owner.Body.IsIncapacitated) {
                    SetMood(s, cond, IconId.CFood, T.Starving, -30);
                }
            }
            if (ticks % Consts.TicksPerMinute != 0) { return; }
            if (cond.Severity <= Severity.Mild) { return; }
            var expSev = cond.Severity - 1;
            var source = NamedEntity.For(T.CondStarvation);
            foreach (var slot in cond.Owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                if (slot.Type.IdH == BodyPartIdH.Stomach) {
                    slot.TryAddOrUpdateWound(source, DamageType.Malnutrition,
                        expSev, ticks, 1f, WoundFlags.CantHeal, conditionId: IdH);
                } else if (slot.Type.IdH == BodyPartIdH.Intestines) {
                    slot.TryAddOrUpdateWound(source, DamageType.Malnutrition,
                        expSev, ticks, 1f, WoundFlags.CantHeal, conditionId: IdH);
                } else if (slot.IsEssential) {
                    if (s.Rng.Chance(0.02f * (int) expSev)) {
                        slot.TryAddOrUpdateWound(source, DamageType.Malnutrition,
                            expSev, ticks, 1f, WoundFlags.CantHeal, conditionId: IdH);
                    }
                }
            }
        }

        private ConditionStarvation() : base(ConditionId.Starvation, IconId.CFood) {}
    }
}
