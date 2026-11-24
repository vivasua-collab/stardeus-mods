using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedOxygen : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedOxygen());
        }

        private float o2ConsumptionPerTick;
        private const float NormalO2 = 0.21f;
        public override int[] RelatedStats => new int[] {
            StatIdH.O2,
            StatIdH.O2Min,
            StatIdH.LungCapacity,
            StatIdH.LungEfficiency,
        };

        private readonly Curve hypoxiaCurve = new float[] {
            0f, 0.5f / Consts.TicksPer15Minutes,
            0.01f, 0.2f / Consts.TicksPer20Minutes,
            0.05f, 0.1f / Consts.TicksPer30Minutes,
            0.10f, 0.05f / Consts.TicksPerHour,
            0.15f, 0.005f / Consts.TicksPer2Hours,
            0.17f, 0f,
        };

        private readonly Curve lungEffCurve = new float[] {
            0f, 0f,
            0.1f, 0.5f,
            0.2f, 0.8f,
            0.5f, 1f,
        };

        private const float hypoxiaRecoveryRate = 1f / Consts.TicksPer15Minutes;

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CLowO2;
            priority = 5f;
            return data.StatsBeing.O2.Value < Consts.MinComfyOxygen;
        }

        // O2 is the ambient level, so normal air is a fully met need
        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = Maths.Clamp(data.StatsBeing.O2.Value / NormalO2, 0f, 1f);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            if (ticks % 10 != 5) {
                // we only tick oxygen on tick 5
                // vents are regenerated on tick 7, sim runs on tick 9
                return;
            }
            var sb = data.StatsBeing;
            float o2;
            bool seekSafety = false;
            if (sb.SpaceSuitIntegrity.Value < 0.99999f) {
                float consumed = ConsumeO2(s, sb, data.Being.PosIdx);
                if (consumed > 0f) {
                    o2 = sb.O2.Value;
                } else {
                    o2 = 0f;
                    seekSafety = true;
                }
            } else {
                o2 = NormalO2;
                sb.O2.UpdateBase(o2);
            }
            o2 *= lungEffCurve.Evaluate(sb.LungEfficiency.Value);
            var hypoxiaDevelopmentRate = hypoxiaCurve.Evaluate(o2) * 10f;
            sbyte direction;
            if (hypoxiaDevelopmentRate > 0f) {
                direction = 1;
                if (!data.HasCondition(ConditionIdH.Hypoxia) && ticks % Consts.TicksPerHour == 0) {
                    if (data.ConditionProgress.TryGetValue(ConditionIdH.Hypoxia, out var prog)) {
                        s.Sys.Log.AddLine(data, T.DevelopingCondition_V2.F(
                            T.CondHypoxia, Units.Percentage(prog)));
                    }
                }
            } else {
                direction = -1;
            }
            Condition.Advance(s, ticks, ConditionIdH.Hypoxia,
                    data, direction,
                    hypoxiaDevelopmentRate,
                    hypoxiaRecoveryRate,
                    seekSafety);
        }

        // returns how much we wanted to consume
        public float ConsumeO2(GameState S, StatsBeing stats, int pos) {
            var consumption = stats.LungCapacity.Value * o2ConsumptionPerTick;
            if (consumption > 0f) {
                stats.O2.UpdateBase(S.ReduceO2(pos, consumption));
            } else {
                stats.O2.UpdateBase(S.GetO2(pos));
            }
            return consumption;
        }

        public override void InitializeFor(BeingData data) {
            o2ConsumptionPerTick = Tunables.O2ConsumptionPerHourBase / Consts.TicksPerHour;
            // we run this once per 10 ticks
            o2ConsumptionPerTick *= 10;
        }

        public override void InitializeForNew(BeingData data) {
            data.StatsBeing.O2Min.UpdateBase(Consts.MinOxygen);
            data.StatsBeing.O2.UpdateBase(NormalO2);
        }

        public AINeedOxygen() : base(NeedId.Oxygen) {}
    }
}
