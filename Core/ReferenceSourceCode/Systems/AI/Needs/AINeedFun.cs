using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Systems.Suggestions;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedFun : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedFun());
        }
        public override bool RequiresMentalHealth => true;
        public override int[] RelatedStats => new int[] {
            StatIdH.Fun,
            StatIdH.FunDropRate,
            StatIdH.FunIncreaseRate,
            StatIdH.DepressionTendency,
        };
        private readonly Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };
        private readonly Curve depressionCurve = new float[] {
            0f, 0.1f / Consts.TicksPerDay,
            0.1f, 0.05f / Consts.TicksPerDay,
            0.25f, 0.01f / Consts.TicksPerDay,
            0.33f, 0f,
        };
        private const float depressionRecoveryRate = 1f / Consts.TicksPer2Days;

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CMoodBad;
            priority = 1f;
            return data.StatsBeing.Fun.Value < 0.2f;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = MainStatSatisfaction(data.StatsBeing.Fun);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            if (data.HasCondition(ConditionIdH.Sleeping)) {
                // make sure being won't interrupt sleep for fun
                if (data.Being.AIAgent.BB.TryNeedGoal(IdH, out var g)) {
                    if (g.InterruptPriority > 1) {
                        // fun should not interrupt sleep; also apply generous grace period
                        g.WithInterrupt(1, Consts.TicksPer8Hours);
                    }
                }
                return;
            }
            if (data.Body.IsIncapacitated) { return; }
            var stats = data.StatsBeing;
            if (LovesCurrentTask(data)) {
                stats.Fun.TryIncrease(stats.FunIncreaseRate.Value);
            } else {
                stats.Fun.TryDrain(stats.FunDropRate.Value, ignoreZero: true);
                if (data.Being != null) {
                    CheckSatisfyGoal(s, data.Being.AIAgent, ticks, stats.Fun.Value);
                }
            }

            if (data.Mind.Health == null) {
                #if PRODUCTION_BUILD
                D.Warn("Being has fun need without mental health! {0} | mind species: {1}",
                    data, data.Mind.Species);
                #else
                D.Err("Being has fun need without mental health! {0} | mind species: {1}",
                    data, data.Mind.Species);
                #endif
                return;
            }
            if (data.Mind.Health.IsEnlightened) { return; }

            var funVal = stats.Fun.Value;
            if (funVal > 0.5f) {
                Condition.Advance(s, ticks, ConditionIdH.Depression,
                    data, -1,
                    0f,
                    depressionRecoveryRate,
                    false,
                    Severity.Critical);
            } else {
                var depressionRate = depressionCurve.Evaluate(funVal)
                    * (1f + stats.DepressionTendency.Value);
                if (depressionRate > 0.00001f) {
                    if (funVal < 0.05f && ticks % Consts.TicksPer12Hours == 0 &&
                            !data.HasCondition(ConditionIdH.Depression)) {
                        if (data.ConditionProgress.TryGetValue(ConditionIdH.Depression, out var prog)) {
                            s.Sys.Log.AddLine(data, T.DevelopingCondition_V2.F(
                                T.CondDepression, Units.Percentage(prog)));
                        }
                    }
                    Condition.Advance(s, ticks, ConditionIdH.Depression,
                        data, 1,
                        depressionRate,
                        0f,
                        false,
                        Severity.Critical);
                }
            }
        }

        private void CheckSatisfyGoal(GameState s, AIAgentComp agent, long ticks,
                float needValue) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CMoodGood, T.NeedFun, agent, ticks)
                        .WithWorldToDesired(AIVarsH.HadFun, false, true)
                        .WriteInitialState();
                agent.BB.SetNeedGoal(IdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (needValue > FunSession.GoalThreshold) {
                // we'll never have fun when over 90%
                if (g.State != AIGoalState.Completed) {
                    g.ChangeStateTo(AIGoalState.Completed, ticks);
                    g.ResetWorldState();
                }
                return;
            }

            SetAvailableWithCooldown(g, agent, ticks);
            // we want to map 0-1 to 0-5
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.05f) {
                // critically unhappy (depression onset) — may interrupt colony work after a grace period
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer4Hours);
            } else {
                // otherwise fun is idle-only and never interrupts
                g.GoalImportance = 0;
                g.WithInterrupt(1, Consts.TicksPer8Hours);
            }
        }

        private bool LovesCurrentTask(BeingData data) {
            var goal = data.Being.AIAgent.Goal;
            if (goal == null) {
                return false;
            }
            var jobId = goal.JobIdH;
            if (jobId == 0) {
                return false;
            }
            if (data.Mind.Jobs.TryJob(jobId, out var jobSlot)) {
                return jobSlot.Loves;
            }
            return false;
        }

        public override void InitializeFor(BeingData data) {
            data.StatsBeing.Fun.CompoundLimit = 1f;
            data.StatsBeing.Fun.IsUnused = false;
        }

        public override void InitializeForNew(BeingData data) {
        }

        public AINeedFun() : base(NeedId.Fun) {}
    }
}
