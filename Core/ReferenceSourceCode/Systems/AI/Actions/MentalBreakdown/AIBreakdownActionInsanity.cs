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
    public sealed class AIBreakdownActionInsanity : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionInsanity());
        }
        public const string ActId = "BreakdownInsanity";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownBreakdowninsanityDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalWentInsane;
        private AIBreakdownActionInsanity() : base(ActId, () => T.BreakdownBreakdowninsanity) {
            WithoutRequiredAbilities();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.TicksInCurrentAction < Consts.TicksPer3Days) {
                agent.BB.ClearMoveTarget();
                if (Rng.UChance(0.95f)) {
                    return InProgress;
                }
                if (!AIActionIdleRoam.TryRoam(agent, false, Rng.URange(2, 4), true)) {
                    // D.Warn("Failed to roam while insane: {0}", agent);
                }
                return InProgress;
            }
            return EndBreakdown(agent, goal);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // sanity target
            agent.BB.Float1 = Rng.URange(0.5f, 0.6f);
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(
                AIActionIdleRoam.StatModRoam, false);
            agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                ticks, Consts.TicksPer3Days, NameT, -100));
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
