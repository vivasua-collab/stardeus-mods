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
using UnityEngine;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AINeedSleep : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedSleep());
        }
        public override int[] RelatedStats => new int[] {
            StatIdH.Sleep,
            StatIdH.SleepPressureRate,
            StatIdH.SleepRate,
        };

        private static readonly Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CSleep;
            priority = 2f;
            return data.StatsBeing.Sleep.Value < 0.15f;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = MainStatSatisfaction(data.StatsBeing.Sleep);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            if (data.HasCondition(ConditionIdH.Sleeping)) {
                return;
            }
            if (data.Body.IsIncapacitated) { return; }
            var stats = data.StatsBeing;
            var rate = stats.SleepPressureRate.Value;
            var deprived = !stats.Sleep.TryDrain(rate);
            if (ticks % Consts.TicksPer15Minutes == 0) {
                // Permanent while there is no sleep left to drain, removed by the same check
                // once the being has slept
                data.Mind.Health?.SetPermanentEffect(s, MoodSourceIdH.SleepDeprivation,
                    IconId.CSleep, T.SleepDeprivation, deprived ? -12 : 0);
            }
            CheckSatisfyGoal(data.Being.AIAgent, ticks, stats.Sleep.Value);
        }

        public static bool MakeThirsty(AIAgentComp agent, long ticks, float needValue) {
            if (!agent.BB.TryNeedGoal(NeedIdH.Drink, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CCoffeeCup, T.VerbDrink, agent, ticks)
                    .WithWorldToDesired(AIVarsH.IsThirsty, true, false)
                    .WriteInitialState();
                agent.BB.SetNeedGoal(NeedIdH.Drink, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return false;
            }

            if (g.State != AIGoalState.Available) {
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.CreatedAt = ticks;
                g.ResetWorldState();
                g.GoalImportance = 0;
            }

            // we want to map 0-1 inversely to 5-0
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.1f) {
                g.GoalImportance = 2;
            }
            return true;
        }

        private void CheckSatisfyGoal(AIAgentComp agent, long ticks, float needValue) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CSleep, T.NeedSleep, agent, ticks)
                    .WithWorldToDesired(AIVarsH.HasSlept, false, true)
                    .WriteInitialState();
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

            SetAvailableWithCooldown(g, agent, ticks, Consts.TicksPer30Minutes,
                Consts.TicksPerHour);
            g.GoalImportance = 0;
            // we want to map 0-1 inversely to 5-0
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.02f) {
                // dangerously sleep deprived — interrupt soon
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer15Minutes);
            } else if (needValue < 0.05f) {
                // very tired
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer30Minutes);
            } else if (needValue < 0.1f) {
                // tired — allow 4 hours grace
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer2Hours);
            } else {
                // just sleepy — let tasks run 3 hours before even considering interruption
                g.GoalImportance = 0;
                g.WithInterrupt(1, Consts.TicksPer3Hours);
            }
        }

        public override void InitializeFor(BeingData data) {
            data.StatsBeing.Sleep.CompoundLimit = 1f;
        }

        public override void InitializeForNew(BeingData data) {
            data.StatsBeing.Sleep.UpdateBase(Rng.URange(0.5f, 0.9f));
        }

        public AINeedSleep() : base(NeedId.Sleep) {}
    }
}
