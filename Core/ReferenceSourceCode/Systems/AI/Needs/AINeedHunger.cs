using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedHunger : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedHunger());
        }
        private Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };
        public override int[] RelatedStats => new int[] {
            StatIdH.Hunger,
            StatIdH.HungerRate,
            StatIdH.StomachCapacity,
            StatIdH.StomachContents,
            StatIdH.IntestinesCapacity,
            StatIdH.IntestinesContents,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CFood;
            priority = 3f;
            return CombinedHungerFor(data.StatsBeing) < 0.2f;
        }

        // Combined, not the raw Hunger stat: right after a meal Hunger is still low while the
        // stomach is full, and the being is not hungry by any check that acts on it
        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = Maths.Clamp(CombinedHungerFor(data.StatsBeing), 0f, 1f);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var stats = data.StatsBeing;
            // how fast we reduce / increase hunger
            var hungerRate = stats.HungerRate.Value;
            float overflow = 0f;
            SetCapacities(data);
            // if anything is in intestines, we will drive hunger up (good)
            if (stats.IntestinesContents.TryDrain(hungerRate, out var removedFromGuts)) {
                var removedFromGutsPercent =
                    removedFromGuts / stats.IntestinesContents.CompoundLimit;
                overflow += stats.Hunger.AddToBaseWithCompoundLimit(removedFromGutsPercent);
                // 25% of what was removed from guts goes to toilet
                overflow += removedFromGuts * 0.25f;
            } else {
                if (stats.Hunger.TryDrain(hungerRate)) {
                    // all good, we are not starving yet
                } else {
                    // this is BAD
                    // add starvation condition
                }
            }

            var digestRate = stats.DigestionRate.Value;
            // move any stomach contents into intestines
            if (stats.StomachContents.TryDrain(digestRate, out var reducedBy)) {
                overflow += stats.IntestinesContents.AddToBaseWithCompoundLimit(reducedBy);
            }

            if (overflow > 0f && !AIActionUseToilet.IsSeated(data)) {
                stats.Toilet.TryDrain(overflow);
            }

            // D.Warn("Hunger: {0}", stats.Hunger.Value);
            AdvanceStarvation(s, data, stats.Hunger.Value, ticks);

            var combinedHunger = CombinedHungerFor(stats);
            CheckSatisfyGoal(data.Being.AIAgent, ticks, combinedHunger);

            HandleSpoonFeeding(data, ticks, stats);

            // if we have undeveloped food poisoning, let's slowly move it down
            if (data.ConditionProgress.ContainsKey(ConditionIdH.FoodPoisoning)) {
                if (!data.HasCondition(ConditionIdH.FoodPoisoning)) {
                    var unpoisonRatePerTick = 0.25f / Consts.TicksPerDay;
                    Condition.Advance(s, ticks, ConditionIdH.FoodPoisoning, data,
                        -1, 0f, unpoisonRatePerTick, false);
                }
            }
        }

        public override void OnRemoveNeed(BeingData data) {
            CancelFeedingGoal(data, G.Ticks);
        }

        // Recovering in bed, or unable to move at all and so unable to fetch food (23XKG)
        public static bool NeedsSpoonFeeding(BeingData data) {
            if (data.Being == null) { return false; }
            if (data.IsBedridden) { return true; }
            return data.HasCondition(ConditionIdH.Injured)
                && data.Being.AIAgent.CurrentAction?.IdH == AIActionSleepInBed.ActIdH;
        }

        private static void HandleSpoonFeeding(BeingData data, long ticks, StatsBeing stats) {
            if (!NeedsSpoonFeeding(data)) {
                // cancel the goal
                CancelFeedingGoal(data, ticks);
                return;
            }
            if (stats.Hunger.Value >= 0.5f || data.Being.Goal != null) {
                return;
            }
            var goal = AIGoal.Create(JobTypeIdH.Medical, T.FeedPatient,
                    data.Being.Conditions, ticks)
                .WithWorldToDesired(AIVarsH.IsPatientFed, false, true)
                .WithPriority(4)
                .WithIcon(IconId.CFeeding)
                .WithRequiredAbilities(AbilityIdH.Carry)
                .WriteInitialState();
            // goal.InterruptPriority = 2;
            if (!data.Being.TrySetGoal(goal)) {
                D.Err("Failed setting feeding goal on being: {0} | {1}", data, goal);
            }
        }

        private static void CancelFeedingGoal(BeingData data, long ticks) {
            if (data.Being.Conditions.TryGetGoal(out var g)) {
                if (g.IconId == IconId.CFeeding) {
                    AIGoal.Cancel(g, ticks, T.AdRejCancelled);
                }
            }
        }

        // we only satisfy hunger if not just hunger is low, but also stomach contents
        // and intestines contents are empty, since hunger goes
        // through stomach -> intestines chain
        public static float CombinedHungerFor(StatsBeing stats) {
            var h = stats.Hunger.Value;
            var s = stats.StomachContents.Value;
            var i = stats.IntestinesContents.Value;
            var v = Maths.Max(h, s);
            v = Maths.Max(v, i);
            return v;
        }

        private void CheckSatisfyGoal(AIAgentComp agent, long ticks, float hunger) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CFood,
                    T.Eat, agent, ticks)
                    .WithWorldToDesired(AIVarsH.IsHungry, true, false)
                    .WriteInitialState();
                agent.BB.SetNeedGoal(IdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (hunger > 0.6f) {
                if (g.State != AIGoalState.Completed) {
                    g.ChangeStateTo(AIGoalState.Completed, ticks);
                    g.ResetWorldState();
                }
                return;
            }

            SetAvailableWithCooldown(g, agent, ticks);
            g.GoalImportance = 0;

            // we want to map 0-1 to 0-5
            g.GoalPriority = (byte) (priorityCurve.Evaluate(hunger) + 0.5f);
            if (hunger < 0.05f) {
                // starvation territory — interrupt soon
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer10Minutes);
            } else if (hunger < 0.1f) {
                // very hungry — 30 min grace
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer30Minutes);
            } else if (hunger < 0.2f) {
                // hungry — 2 hour grace
                g.GoalImportance = 1;
                g.WithInterrupt(2, Consts.TicksPer2Hours);
            } else {
                // peckish — let tasks run 6 hours before considering interruption
                g.GoalImportance = 0;
                g.WithInterrupt(1, Consts.TicksPer6Hours);
            }
        }


        private void AdvanceStarvation(GameState s, BeingData data, float hunger, long ticks) {
            sbyte direction;
            if (hunger > 0.2f) {
                direction = -1;
            } else if (hunger < 0.05f) {
                direction = 1;
                if (!data.HasCondition(ConditionIdH.Starvation) && ticks % Consts.TicksPer12Hours == 0) {
                    if (data.ConditionProgress.TryGetValue(ConditionIdH.Starvation, out var prog)) {
                        s.Sys.Log.AddLine(data, T.DevelopingCondition_V2.F(
                            T.CondStarvation, Units.Percentage(prog)));
                    }
                }
            } else {
                return;
            }
            var devRate = 1f / (Tunables.StarvationDevelopmentRateDays * Consts.TicksPerDay);
            var recRate = 1f / (Tunables.StarvationRecoveryRateHours * Consts.TicksPerHour);
            // D.Warn("Advancing starvation with hunger: {0}-> {1}. Dev rate: {2}. Rec rate: {3}",
            //     hunger, direction, devRate, recRate);
            Condition.Advance(s, ticks, ConditionIdH.Starvation,
                data, direction, devRate, recRate, false);
        }


        public override void InitializeFor(BeingData data) {
            SetCapacities(data);
            data.StatsBeing.Hunger.CompoundLimit = 1f;
        }
        public override void InitializeForNew(BeingData data) {
            SetCapacities(data);
            data.StatsBeing.IntestinesContents.UpdateBase(0f);
            data.StatsBeing.StomachContents.UpdateBase(0f);
            data.StatsBeing.Hunger.UpdateBase(Rng.URange(0.5f, 0.9f));
        }

        private static void SetCapacities(BeingData data) {
            data.StatsBeing.IntestinesContents.CompoundLimit = data.StatsBeing.IntestinesCapacity.CompoundLimit;
            data.StatsBeing.StomachContents.CompoundLimit = data.StatsBeing.StomachCapacity.CompoundLimit;
        }

        public AINeedHunger() : base(NeedId.Hunger) {}
    }
}
