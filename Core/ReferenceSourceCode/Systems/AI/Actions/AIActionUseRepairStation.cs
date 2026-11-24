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

namespace Game.Systems.AI {
    public sealed class AIActionUseRepairStation : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseRepairStation());
        }
        public const string ActId = "UseRepairStation";
        public static int ActIdH;
        protected override string FailMsgSlotUnavailable => T.InfoRepairStationUnavailable;
        protected override int SlotDesignation => SlotDesignationH.BotRepair;
        protected override bool CanBeMakeshift => false;
        protected override int failReachCooldownFrom => Consts.TicksPer30Minutes;
        protected override int failReachCooldownTo => Consts.TicksPerHour;
        protected override Func<ISlots, bool> PreferredSlotCheck =>
            RepairStationComp.IsUnobstructed;
        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BeingData.Body.CanHeal) {
                return false;
            }
            goal.WithWorld(AIVarsH.IsBotDamaged, false);
            return true;
        }

        protected override AIActionResult OnStartUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            agent.HideThoughtBubble();
            if (slot.Parent is RepairStationComp repStation) {
                repStation.StartRepairing(agent.Being);
            } else {
                return Error("Slot is not repair station!");
            }
            return InProgress;
        }

        protected override void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (slot.Parent is RepairStationComp repStation) {
                repStation.StopRepairing();
            }

            base.OnEndUsingSlot(agent, goal, slot, ticks);
        }

        private AIActionUseRepairStation() : base(ActId, () => T.JobTypeMaintenance) {
            Preconditions = AIState.With(AIVarsH.IsBotDamaged, true);
            Outcomes = AIState.With(AIVarsH.IsBotDamaged, false);
            // We can force this action for an immobilized being
            // WithRequiredAbilities(AbilityIdH.Move);
            WithoutRequiredAbilities();
            WithRequiredFlags(StatFlags.Robotic);
            MarkAsRest();
            ActIdH = IdH;
        }

        protected override AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
            return base.OnSlotUnavailable(agent, goal, ticks);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            return 1f;
        }

        public static AIGoalCheckResult CheckDoable(GameState S, AIGoal goal) {
            if (!S.Sys.Slots.HasFreeFor(SlotDesignationH.BotRepair)) {
                return AIGoalCheckResult.Fail(T.InfoRepairStationUnavailable, true, IconId.CWarning);
            }
            return AIGoalCheckResult.Doable;
        }
    }
}
