using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedTemperature : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedTemperature());
        }
        private readonly Curve conditionCurve = new float[] {
            300f, 0.5f / Consts.TicksPer10Minutes,
            100f, 0.1f / Consts.TicksPer30Minutes,
            50f, 0.05f / Consts.TicksPerHour,
            10f, 0.01f / Consts.TicksPerHour,
            0f, 0.005f / Consts.TicksPerHour,
        };
        private const float conditionRecoveryRate = 1f / Consts.TicksPer2Hours;
        // How far outside the comfort range the satisfaction bar empties. Around 50 K out of
        // range conditionCurve takes hypo/hyperthermia from days to under a day to develop.
        private const float ComfortSpanK = 50f;
        public override int[] RelatedStats => new int[] {
            StatIdH.Temp,
            StatIdH.TempMin,
            StatIdH.TempMax,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CTemperature;
            priority = 4f;
            var temp = data.StatsBeing.Temp.Value;
            var tempMin = data.StatsBeing.TempMin.Value;
            var tempMax = data.StatsBeing.TempMax.Value;
            sbyte directionHyper;
            sbyte directionHypo;
            EstimateConditionRates(temp, tempMin, tempMax, out directionHyper,
                out directionHypo, out _, out _);
            return directionHyper == 1 || directionHypo == 1;
        }

        // Full anywhere inside the comfort range, since the being does not care where in it the
        // temperature sits. Same inputs as RequiresAttention, so the bar drops exactly when the
        // name turns red.
        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            var stats = data.StatsBeing;
            var temp = stats.Temp.Value;
            float outside;
            if (temp < stats.TempMin.Value) {
                outside = stats.TempMin.Value - temp;
            } else if (temp > stats.TempMax.Value) {
                outside = temp - stats.TempMax.Value;
            } else {
                outside = 0f;
            }
            satisfaction = Maths.Clamp(1f - outside / ComfortSpanK, 0f, 1f);
            return true;
        }

        public override string SatisfactionText(BeingData data, float satisfaction) {
            return Texts.Temperature(data.StatsBeing.Temp.Value);
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var sb = data.StatsBeing;
            var temp = s.GetTemp(data.Being.PosIdx);
            var tempMin = sb.TempMin.Value;
            var tempMax = sb.TempMax.Value;

            var flam = data.Being.Flammable;
            if (!flam.IsOnFire && temp > flam.Flam.SelfCombustTempKelvin
                    && temp > tempMax) {
                if (s.GetO2(data.Being.PosIdx) > 0f) {
                    var tempDiff = temp - flam.Flam.SelfCombustTempKelvin;
                    var fireChance = Mathf.InverseLerp(0.01f, 1f, tempDiff / 50f);
                    // D.Warn("temp > temp max for being {0} > {1}: {2}", temp, tempMax, data);
                    if (Rng.UChance(fireChance)) {
                        var fuel = Mathf.InverseLerp(0f, 2f, tempDiff / 100f);
                        var fire = Fire.Create(1f, fuel);
                        s.Sys.Firex.TrySetOnFire(flam, fire);
                    }
                }
            }

            if (data.Species.IsBiological) {
                ProcessBioConditions(s, data, ticks, temp, tempMin, tempMax);
            }
            if (temp > tempMax) {
                // deal heat damage to robots
                var tempDiff = temp - flam.Flam.SelfCombustTempKelvin;
                if (Rng.UChance(tempDiff / 100f)) {
                    var sev = (Severity) ((byte) tempDiff / 50f);
                    if (sev < Severity.Mild) {
                        sev = Severity.Mild;
                    }
                    BeingUtils.DamageRandomBodyPart(s, data, Rng.Unseeded,
                        DamageType.Heat, sev, NamedEntity.For(T.Environment),
                            tempDiff / 100f);
                }
            }
        }

        private void ProcessBioConditions(GameState s, BeingData data,
                long ticks, float temp, float tempMin, float tempMax) {
            sbyte directionHyper;
            sbyte directionHypo;
            float rateHyper;
            float rateHypo;
            EstimateConditionRates(temp, tempMin, tempMax, out directionHyper,
                out directionHypo, out rateHyper, out rateHypo);
            Condition.Advance(s, ticks, ConditionIdH.Hypothermia,
                data, directionHypo,
                rateHypo,
                conditionRecoveryRate,
                true);
            Condition.Advance(s, ticks, ConditionIdH.Hyperthermia,
                data, directionHyper,
                rateHyper,
                conditionRecoveryRate,
                true);
            if (directionHypo == 1 && !data.HasCondition(ConditionIdH.Hypothermia)
                    && ticks % Consts.TicksPerHour == 0) {
                if (data.ConditionProgress.TryGetValue(ConditionIdH.Hypothermia, out var prog)) {
                    s.Sys.Log.AddLine(data, T.DevelopingCondition_V2.F(
                        T.CondHypothermia, Units.Percentage(prog)));
                }
            }
            if (directionHyper == 1 && !data.HasCondition(ConditionIdH.Hyperthermia)
                    && ticks % Consts.TicksPerHour == 0) {
                if (data.ConditionProgress.TryGetValue(ConditionIdH.Hyperthermia, out var prog)) {
                    s.Sys.Log.AddLine(data, T.DevelopingCondition_V2.F(
                        T.CondHyperthermia, Units.Percentage(prog)));
                }
            }
        }

        private void EstimateConditionRates(float temp, float tempMin, float tempMax, out sbyte directionHyper, out sbyte directionHypo, out float rateHyper, out float rateHypo) {
            if (temp < tempMin) {
                // hypothermia
                var diff = -(temp - tempMin);
                rateHypo = conditionCurve.Evaluate(diff);
                rateHyper = 0f;
                directionHyper = -1;
                directionHypo = 1;
            } else if (temp > tempMax) {
                // hyperthermia
                var diff = temp - tempMax;
                rateHyper = conditionCurve.Evaluate(diff);
                rateHypo = 0f;
                directionHyper = 1;
                directionHypo = -1;
            } else {
                // recovery
                // AdvanceCondition(s, ConditionIdH.Damaged)
                rateHyper = 0f;
                rateHypo = 0f;
                directionHyper = -1;
                directionHypo = -1;
            }
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        public AINeedTemperature() : base(NeedId.Temperature) {}
    }
}
