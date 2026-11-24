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

namespace Game.Systems.AI {
    public sealed class AIActionRecharge : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionRecharge());
        }
        public const string ActId = "Recharge";
        public readonly static int ActIdH = Hashes.S(ActId);
        protected override string FailMsgSlotUnavailable => T.InfoChargeStationUnavailable;
        protected override int SlotDesignation => SlotDesignationH.Charging;
        protected override bool CanBeMakeshift => false;
        protected override int failReachCooldownFrom => Consts.TicksPer30Minutes;
        protected override int failReachCooldownTo => Consts.TicksPer1Hours;
        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            var stats = agent.BeingData.StatsBeing;
            if (stats.BatteryCharge.Value >= stats.BatteryCapacity.Value) {
                goal.WithWorld(AIVarsH.IsRecharged, true);
                return true;
            }
            return false;
        }

        protected override void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (slot.Parent is ChargeStationComp cs) {
                cs.StopCharging();
            } else {
                D.Err("Not a charge station! {0}", slot.Parent);
            }
        }

        private AIActionRecharge() : base(ActId, () => T.ActRecharge) {
            Preconditions = AIState.With(AIVarsH.IsRecharged, false);
            Outcomes = AIState.With(AIVarsH.IsRecharged, true);
            WithRequiredAbilities(AbilityIdH.Recharge, AbilityIdH.Move);
            MarkAsRest();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return (agent.Being.Data.StatsBeing.BatteryCharge.Value + 0.001f) * 10f;
        }
    }
}
