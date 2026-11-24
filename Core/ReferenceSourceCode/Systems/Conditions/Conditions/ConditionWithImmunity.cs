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
    /// <summary>
    /// In other words, ConditionDisease
    /// </summary>
    public abstract class ConditionWithImmunity : Condition {
        protected Curve diseaseTransmissionSpeedByAgeCurve;
        public float Virality;
        public readonly int ImmunityId;
        protected ConditionWithImmunity(string id, string immunityId, float virality, string icon) : base(id, icon) {
            if (immunityId == null) {
                D.Err("ImmunityId cannot be null! {0}", this);
                return;
            }
            Virality = virality;
            ImmunityId = Hashes.S(immunityId);
            diseaseTransmissionSpeedByAgeCurve = Tunables.DiseaseTransmissionSpeedByAgeCurve;
        }

        protected abstract void OnTick(GameState s, ConditionState cond, long ticks);

        public sealed override void Tick(GameState s, ConditionState cond, long ticks) {
            // check if by chance the condition has already been removed by the immunity
            if (!cond.Owner.HasCondition(IdH)) {
                D.Err("Ticking a condition that was removed! / not added! {0}", cond);
                return;
            }
            // gain immunity
            var immunityGainRate = cond.Owner.StatsBeing.ImmunityGainRate.Value;
            // we have to multiply this by 3 because we have 3 levels of
            // immunity and we show them as percentage
            immunityGainRate *= 3;
            immunityGainRate /= Consts.TicksPerDay;
            // D.Warn("Immunity gain rate: {0} | {1}", immunityGainRate,
            //     cond.Owner.StatsBeing.ImmunityGainRate.ToString());

            if (!cond.Owner.HasCondition(ImmunityId)) {
                // see 0% immunity 3x faster
                immunityGainRate *= 3f;
            }
            // advance immunity
            Advance(s, ticks, ImmunityId, cond.Owner, 1,
                immunityGainRate, 0f, false, Severity.Critical);

            TryInfect(s, cond, ticks);

            OnTick(s, cond, ticks);
        }
        protected void TryInfect(Being b, Severity sev, long ticks) {
            if (Virality < Maths.Epsilon) { return; }
            if (b.IsDead) { return; }
            if (!b.Species.IsBiological) { return; }
            if (b.Data.HasCondition(IdH)) { return; }
            if (b.Data.HasCondition(ImmunityId)) { return; }
            var infectRatePerEncounter = diseaseTransmissionSpeedByAgeCurve.Evaluate(
                b.Data.Body.AgePercent) * Virality;
            if (Advance(b.S, ticks, IdH, b.Data, 1,
                    infectRatePerEncounter, 0f, false, Severity.Critical)) {
                // D.Warn("Infected with {0}: {1}", NameT, b);
            }
        }
        protected void TryInfect(GameState s, ConditionState cond, long ticks) {
            var b = cond.Owner.Being;
            if (b == null) { return; }
            var pos = b.PosIdx;
            var sev = cond.Severity;
            if (s.Beings.ByPos.TryGetValue(pos, out var others)) {
                if (others.Count > 1) {
                    foreach (var o in others) {
                        if (o == b) { continue; }
                        TryInfect(o, sev, ticks);
                    }
                }
            }
        }
    }
}
