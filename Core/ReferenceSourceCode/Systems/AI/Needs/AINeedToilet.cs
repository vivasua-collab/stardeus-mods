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
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AINeedToilet : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedToilet());
        }
        public override int[] RelatedStats => new int[] {
            StatIdH.Toilet,
            StatIdH.ToiletRate,
            StatIdH.ToiletReliefRate,
        };

        private readonly Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CToilet;
            priority = 1f;
            return data.StatsBeing.Toilet.Value < 0.05f;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = MainStatSatisfaction(data.StatsBeing.Toilet);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var stats = data.StatsBeing;
            if (!data.HasAbility(AbilityIdH.UseToilet)) {
                // no usable guts, the need can never be satisfied
                stats.Toilet.UpdateBase(1f);
                CheckSatisfyGoal(data.Being.AIAgent, ticks, stats.Toilet.Value);
                return;
            }
            if (AIActionUseToilet.IsSeated(data)) {
                CheckSatisfyGoal(data.Being.AIAgent, ticks, stats.Toilet.Value);
                return;
            }
            var rate = stats.ToiletRate.Value;
            bool isAsleep = data.HasCondition(ConditionIdH.Sleeping);
            // A sleeper holds it until waking up, but a bedridden being never gets up to
            // use a toilet, so it ends up soiling the bed instead of waiting forever
            bool canHoldIt = isAsleep && !data.IsBedridden;
            if (canHoldIt) {
                if (stats.Toilet.Value < 0.1f) {
                    return;
                }
            }
            if (!stats.Toilet.TryDrain(rate) && Rng.UChance(0.01f)) {
                if (data.Being.AIAgent.CurrentAction?.IdH != AIActionUseToilet.ActIdH) {
                    if (canHoldIt) {
                        D.Err("Trying to soil self while asleep! Toilet: {0} | {1}",
                            stats.Toilet, data);
                    } else {
                        ShitPants(s, data, ticks, stats);
                    }
                }
            }
            CheckSatisfyGoal(data.Being.AIAgent, ticks, stats.Toilet.Value);
        }

        private void ShitPants(GameState s, BeingData data, long ticks, StatsBeing stats) {
            // shit ourselves
            SpawnPoop(s, data.Being);
            // #if !PRODUCTION_BUILD
            // EntityUtils.DebugPauseFocusOn(data.Being, "Soiled");
            // #endif
            // if (data.Being?.AIAgent.BB.TryNeedGoal(IdH, out var g) != true) {
            //     g = null
            // }
            // D.Warn("Shitting itself: {0} | {1} | {2} | Need goal: {3}", stats.Toilet.Value,
            //     data.Being, data.Being?.AIAgent.Goal, D.DumpInstance(g));

            stats.Toilet.AddToBaseWithCompoundLimit(Rng.URange(0.7f, 0.9f));
            // immediately dirty
            if (stats.Hygiene.BaseValue > 0.05f) {
                stats.Hygiene.UpdateBase(0.05f);
            }
            if (data.Mind.Health != null) {
                s.Sig.AddMentalEffect.Send(data, MentalEffect.Create(
                    data.Being.PosIdx, ticks,
                    Consts.TicksPerDay, T.SoiledThemselves, -20));
            }
        }

        private void CheckSatisfyGoal(AIAgentComp agent, long ticks, float needValue) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CToilet,
                    T.UseToilet, agent, ticks)
                    .WithWorldToDesired(AIVarsH.HasUsedToilet, false, true);
                agent.BB.SetNeedGoal(IdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (needValue > 0.2f) {
                // we'll never go to toilet while bladder is under 80%
                if (g.State != AIGoalState.Completed) {
                    g.ChangeStateTo(AIGoalState.Completed, ticks);
                    g.ResetWorldState();
                }
                return;
            }

            SetAvailableWithCooldown(g, agent, ticks, Consts.TicksPer15Minutes,
                Consts.TicksPer30Minutes);
            g.GoalImportance = 0;

            // we want to map 0-1 inversely to 5-0
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.05f) {
                // about to soil self — short grace, interrupt soon
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer15Minutes);
            } else if (needValue < 0.1f) {
                // getting urgent — allow 1h grace
                g.GoalImportance = 1;
                g.WithInterrupt(2, Consts.TicksPerHour);
            } else {
                // not that bad yet — let tasks run for 3 hours before interrupting
                g.GoalImportance = 0;
                g.WithInterrupt(1, Consts.TicksPer3Hours);
            }
        }

        private void SpawnPoop(GameState s, Being being) {
            // FIXME this should be a signal
            being.Data.Body.AddExcrementsOnFloor();
        }

        public override void InitializeFor(BeingData data) {
            data.StatsBeing.Toilet.CompoundLimit = 1f;
        }

        public override void InitializeForNew(BeingData data) {
            data.StatsBeing.Toilet.UpdateBase(Rng.URange(0.5f, 0.9f));
        }

        public AINeedToilet() : base(NeedId.Toilet) {}
    }
}
