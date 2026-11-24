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
using Game.Systems.Mental;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public sealed class AIActionUseShower : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseShower());
        }
        public const string ActId = "UseShower";
        protected override string FailMsgSlotUnavailable => T.NoShower;
        protected override int SlotDesignation => SlotDesignationH.Shower;
        protected override bool CanBeMakeshift => true;
        protected override int failReachCooldownFrom => Consts.TicksPer2Hours;
        protected override int failReachCooldownTo => Consts.TicksPer3Hours;
        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.Being.Data.StatsBeing.Hygiene.Value > 0.99f) {
                // D.Warn("Done using shower");
                goal.WithWorld(AIVarsH.HasCleanedUp, true);
                return true;
            }
            return false;
        }

        protected override AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            // D.Warn("Using shower: {0}", agent.BeingData.StatsBeing.Hygiene.Value);
            var speed = agent.BeingData.StatsBeing.HygieneReliefRate.Value;
            agent.BeingData.StatsBeing.Hygiene.AddToBaseWithCompoundLimit(speed);
            return InProgress;
        }

        private AIActionUseShower() : base(ActId, () => T.ActUseshower) {
            Preconditions = AIState.With(AIVarsH.HasCleanedUp, false);
            Outcomes = AIState.With(AIVarsH.HasCleanedUp, true);
            WithRequiredNeed(NeedIdH.Hygiene);
            WithRequiredAbilities(AbilityIdH.UseShower, AbilityIdH.Move);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.ReservedSlot != null) {
                ApplyShowerBonus(agent, ticks);
            } else {
                ApplyNoShowerPenalty(agent, ticks);
            }
            base.OnDeactivate(agent, goal, ticks);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return (agent.Being.Data.StatsBeing.Hygiene.Value + 0.001f) * 10f;
        }

        private void ApplyNoShowerPenalty(AIAgentComp agent, long ticks) {
            sbyte amt;
            var reason = T.NoShower;
            var traits = agent.BeingData.Mind.Traits;
            if (traits.HasTrait(TraitIdH.Ablutophobic)) {
                amt = 5;
                reason = TraitDef.MoodReason(reason, TraitIdH.Ablutophobic);
            } else if (traits.HasTrait(TraitIdH.Showerer)) {
                amt = -10;
                reason = TraitDef.MoodReason(reason, TraitIdH.Showerer);
            } else {
                amt = -5;
            }
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, reason, amt);
        }

        private void ApplyShowerBonus(AIAgentComp agent, long ticks) {
            sbyte amt;
            var reason = T.FeelingClean;
            var traits = agent.BeingData.Mind.Traits;
            if (traits.HasTrait(TraitIdH.Ablutophobic)) {
                amt = 2;
                reason = TraitDef.MoodReason(reason, TraitIdH.Ablutophobic);
            } else if (traits.HasTrait(TraitIdH.Showerer)) {
                amt = 4;
                reason = TraitDef.MoodReason(reason, TraitIdH.Showerer);
            } else {
                amt = 3;
            }
            AddMentalEffect(agent, ticks, Consts.TicksPer2Hours, reason, amt);
        }
    }
}
