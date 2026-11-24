using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public abstract class ConditionImmunity : Condition {
        public override Priority Importance => Priority.None;
        public readonly int DiseaseId;
        public readonly float ImmunityRate;
        protected ConditionImmunity(string id, string diseaseId, float immunityRate, string icon) : base(id, icon) {
            DiseaseId = Hashes.S(diseaseId);
            ImmunityRate = immunityRate;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.Immunity;
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        public float ImmunityPercent(ConditionState cond) {
            var pct = ((float) cond.Severity - 1f) / 3f;
            if (pct < 1f && cond.Owner.ConditionProgress.TryGetValue(
                    cond.Condition.IdH, out var prog)) {
                pct += prog / 3f;
            }
            return pct;
        }

        public sealed override void Tick(GameState s, ConditionState cond, long ticks) {
            if (cond.Owner.HasCondition(DiseaseId)) {
                var adjImmunityRate = ImmunityRate * ImmunityPercent(cond);
                Advance(s, ticks, DiseaseId, cond.Owner, -1, 0f, adjImmunityRate, false);
                return;
            }
            // lose immunity
            var immunityLossRate = cond.Owner.StatsBeing.ImmunityLossRate.Value;
            // have to multiply by 3 again, since we have 4 levels
            immunityLossRate *= 3;
            immunityLossRate /= Consts.TicksPerDay;
            // D.Warn("Immunity loss rate: {0} | {1}", immunityLossRate,
            //     cond.Owner.StatsBeing.ImmunityLossRate.ToString());

            // advance immunity
            Advance(s, ticks, IdH, cond.Owner, -1,
                0f, immunityLossRate, false, Severity.Critical);

        }
    }
}
