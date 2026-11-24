using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Stats;
using Game.Systems.Conditions;

namespace Game.Systems.AI {
    public sealed class AIActionUseHealingDevice : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseHealingDevice());
        }
        public const string ActId = "UseHealingDevice";
        public static int ActIdH;
        protected override string FailMsgSlotUnavailable => T.AdRejLackingHealer;
        protected override int SlotDesignation => SlotDesignationH.Healing;
        protected override bool CanBeMakeshift => false;
        protected override int failReachCooldownFrom => Consts.TicksPer30Minutes;
        protected override int failReachCooldownTo => Consts.TicksPerHour;
        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BeingData.Body.CanHeal || agent.BeingData.Body.IsIncapacitated) {
                return false;
            }
            goal.WithWorld(AIVarsH.IsInjured, false);
            return true;
        }

        protected override AIActionResult OnStartUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (agent.BeingData.Species.IsBiological) {
                ConditionState.CreateFor(agent.S, ConditionIdH.Sleeping,
                    agent.BeingData, Severity.Irrelevant, true);
            }
            agent.HideThoughtBubble();
            return InProgress;
        }

        protected override void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (agent.BeingData.Species.IsBiological) {
                agent.BeingData.RemoveCondition(agent.S, ConditionIdH.Sleeping);
                if (slot.Parent is AutoDocComp autoDoc) {
                    autoDoc.RemoveModifiers(agent.BeingData.StatsBeing);
                }
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            base.OnDeactivate(agent, goal, ticks);
            agent.BeingData.RemoveCondition(agent.S, ConditionIdH.Sleeping);
        }

        private AIActionUseHealingDevice() : base(ActId, () => T.HealingInjuries) {
            Preconditions = AIState.With(AIVarsH.IsInjured, true);
            Outcomes = AIState.With(AIVarsH.IsInjured, false);
            MarkAsQuiet();
            MarkAsRest();
            // We can force this action for an immobilized being
            // WithRequiredAbilities(AbilityIdH.Move);
            WithoutRequiredAbilities();
            WithRequiredFlags(StatFlags.Biological);
            WithForbiddenFlags(StatFlags.Wild);
            ActIdH = IdH;
        }

        protected override AIActionResult OnUseSlotFail(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            OnEndUsingSlot(agent, goal, slot, ticks);
            return base.OnSlotUnavailable(agent, goal, ticks);
        }

        protected override AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
            return base.OnSlotUnavailable(agent, goal, ticks);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            if (!agent.S.Sys.Slots.HasFor(SlotDesignation)) {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                return 0f;
            }
            return 0.5f;
        }
    }
}
