using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Utils;
using Game.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Components;
using KL.Randomness;
using Game.UI;

namespace Game.Systems.AI {
    public sealed class AINeedCleanFloors : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedCleanFloors());
        }

        public override int[] RelatedStats => null;

        public override void Tick(GameState s, BeingData data, long ticks) {
            if (ticks % Consts.TicksPer5Minutes != 0) { return; }
            if (!data.IsFriendly) { return; }
            if (!data.Mind.Jobs.CanDo(JobTypeIdH.Cleaning, out JobSlot jobSlot)) {
                return;
            }
            if (data.Species.IsBiological) {
                if (data.HasCondition(ConditionIdH.Injured)) { return; }
                if (data.ConditionsPreventColonyWork) { return; }
            }
            if (data.Species.IdH == SpeciesIdH.CleaningBot &&
                    data.Being.TryGetComponent<CleaningBotComp>(out var cb)) {
                if (data.Being.AIAgent.DirectControl.IsUnderDirectControl) {
                    if (data.Being.AIAgent.Jobs.TryJob(JobTypeIdH.Cleaning, out var job)) {
                        // clean the floor underneath!
                        var chance = 0.15f * (1f + job.UseSkill()) * job.WorkSpeedMul();
                        // D.Warn("Clean chance: {0}. eff: {1}", chance, job.Efficiency);
                        if (s.Sys.Dirt.CleanOneAt(data.Being.PosIdx, out var hadDirt, chance)) {
                            if (hadDirt) {
                                BeingUtils.PlaySelectSoundFor(data.Being);
                            }
                        }
                    }
                }
                if (!cb.IsAutoCleanEnabled) {
                    return;
                }
            }
            var agent = data.Being.AIAgent;

            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CMop,
                    T.CleanFloors, agent, ticks)
                    .WithWorld(AIVarsH.IsIdle, false)
                    .WithDesired(AIVarsH.IsFloorClean, true)
                    .WriteInitialState();
                agent.BB.SetNeedGoal(IdH, g);
            }

            g.GoalPriority = jobSlot.Priority;

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (g.State != AIGoalState.Available) {
                if (agent.BB.IsCoolingDown(IdH, ticks)) {
                    return;
                }
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.CreatedAt = ticks;
                g.ResetWorldState();
            }

            if (data.Mind.Species.IdH == SpeciesIdH.CleaningBot) {
                if (agent.IsIdle && agent.CanWork &&
                        !agent.DirectControl.IsUnderDirectControl) {
                    // g.SetAgent(agent);
                    // if (!agent.S.Query.ForceGoal.Ask(g)) {
                    //     D.Err("Could not do cleaning: {0} | {1}", g, agent);
                    // }
                    var planner = agent.S.Query.GetAIPlanner.Ask();
                    if (planner.TryFormulateFor(g, agent, ticks, out var plan)) {
                        agent.SetGoal(g, plan, ticks);
                    } else {
                        agent.BB.SetCooldown(NeedIdH.CleanFloors, ticks,
                            Consts.TicksPer30Minutes, Consts.TicksPerHour);
                    }
                }
            }
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        public AINeedCleanFloors() : base(NeedId.CleanFloors) {}
    }
}
