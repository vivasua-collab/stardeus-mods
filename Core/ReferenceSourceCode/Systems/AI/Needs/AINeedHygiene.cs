using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedHygiene : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedHygiene());
        }
        public override int[] RelatedStats => new int[] {
            StatIdH.Hygiene,
            StatIdH.HygieneRate,
            StatIdH.HygieneReliefRate,
        };

        private readonly Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CShower;
            priority = 1f;
            return data.StatsBeing.Hygiene.Value < 0.1f;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = MainStatSatisfaction(data.StatsBeing.Hygiene);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var stats = data.StatsBeing;
            var rate = stats.HygieneRate.Value;
            if (!stats.Hygiene.TryDrain(rate)) {
                // TODO mood effect
            }
            CheckSatisfyGoal(data.Being.AIAgent, ticks, stats.Hygiene.Value);
        }

        private void CheckSatisfyGoal(AIAgentComp agent, long ticks, float needValue) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CShower, T.VerbCleaningUp, agent, ticks)
                    .WithWorldToDesired(AIVarsH.HasCleanedUp, false, true);
                agent.BB.SetNeedGoal(IdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (needValue > 0.5f) {
                // we'll never sleep over 50%
                if (g.State != AIGoalState.Completed) {
                    g.ChangeStateTo(AIGoalState.Completed, ticks);
                    g.ResetWorldState();
                }
                return;
            }

            SetAvailableWithCooldown(g, agent, ticks, Consts.TicksPer2Hours,
                Consts.TicksPer3Hours);

            // we want to map 0-1 to 0-5
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.05f) {
                g.GoalImportance = 1;
            } else {
                g.GoalImportance = 0;
            }
            // hygiene is not urgent — let short tasks finish before showering
            g.WithInterrupt(1, Consts.TicksPer8Hours);
        }

        public override void InitializeFor(BeingData data) {
            data.StatsBeing.Hygiene.CompoundLimit = 1f;
        }

        public override void InitializeForNew(BeingData data) {
            data.StatsBeing.Hygiene.UpdateBase(Rng.URange(0.5f, 0.9f));
        }

        public AINeedHygiene() : base(NeedId.Hygiene) {}
    }
}
