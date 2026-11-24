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

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionRoam : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionRoam());
        }
        public const string ActId = "BreakdownRoam";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdownroamaroundDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalRoamed;
        private AIBreakdownActionRoam() : base(ActId, () => T.BreakdownBreakdownroamaround) {
            WithRequiredAbilities(AbilityIdH.Move);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.TicksInCurrentAction < Consts.TicksPer12Hours
                    && agent.BeingData.StatsBeing.Sanity.Value < agent.BB.Float1) {
                agent.BB.ClearMoveTarget();
                if (Rng.UChance(0.8f)) {
                    return InProgress;
                }
                if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                    agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                        ticks, Consts.TicksPer4Hours, NameT, 100));
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
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Float1 = 0f;
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
