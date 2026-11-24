using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.AI;
using Game.Systems.Mental;
using Game.Commands;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionStartFires : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionStartFires());
        }
        public const string ActId = "BreakdownStartFires";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownstartfireDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalStartedFires;
        private AIBreakdownActionStartFires() : base(ActId, () => T.BreakdownBreakdownstartfire) {
            WithoutRequiredAbilities();
            WithForbiddenFlags(StatFlags.Animal);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.Long1NotSaved == 0) {
                agent.BB.Long1NotSaved = ticks + Rng.URange(
                    Consts.TicksPer10Minutes, Consts.TicksPer20Minutes);
            } else if (agent.BB.Long1NotSaved < ticks) {
                agent.BB.Long1NotSaved = ticks + Rng.URange(
                    Consts.TicksPer20Minutes, Consts.TicksPerHour);
                agent.BB.Long1NotSaved = 0;
                agent.BB.Byte1++;
                S.CmdQ.Enqueue(new CmdStartFire(Pos.RandomAdjacent(
                    agent.PosIdx, Rng.Unseeded)));
                // S.Situation.AddTension(1f, "Breakdown: Starting fire");
                agent.AddMentalEffect(MentalEffect.Create(
                    agent.PosIdx,
                    ticks, Consts.TicksPer4Hours, NameT, 100));
                return InProgress;
            }

            if (agent.TicksInCurrentAction < Consts.TicksPer6Hours
                    && agent.BB.Byte1 < 10
                    && agent.BeingData.StatsBeing.Sanity.Value < agent.BB.Float1) {
                agent.BB.ClearMoveTarget();
                if (Rng.UChance(0.8f)) {
                    return InProgress;
                }
                if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                    return InProgress;
                } else {
                    var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                    if (pos != Pos.Invalid) {
                        if (agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK)) {
                            return InProgress;
                        }
                    }
                }
            }

            return EndBreakdown(agent, goal);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // sanity target
            agent.BB.Float1 = Rng.URange(0.5f, 0.6f);
            // fire count
            agent.BB.Byte1 = 0;
            // when we start another fire
            agent.BB.Long1NotSaved = 0;
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            AddBreakdownNotification(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Float1 = 0f;
            agent.BB.Byte1 = 0;
            agent.BB.Long1NotSaved = 0;
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
