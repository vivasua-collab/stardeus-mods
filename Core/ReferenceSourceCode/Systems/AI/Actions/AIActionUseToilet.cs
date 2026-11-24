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
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public sealed class AIActionUseToilet : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseToilet());
        }
        public const string ActId = "UseToilet";
        public static int ActIdH = Hashes.S(ActId);
        protected override string FailMsgSlotUnavailable => T.NoToilet;
        protected override int SlotDesignation => SlotDesignationH.Toilet;
        protected override bool CanBeMakeshift => true;
        protected override int failReachCooldownFrom => Consts.TicksPer30Minutes;
        protected override int failReachCooldownTo => Consts.TicksPerHour;

        // Seated, not on the way there. While seated nothing adds to the Toilet need and
        // nothing soils: whatever comes out goes into the toilet, and a slow pooper with a
        // disease used to drain faster than they relieved and sat there for half a day
        public static bool IsSeated(BeingData data) {
            var being = data.Being;
            var agent = being?.AIAgent;
            if (agent == null || agent.CurrentAction?.IdH != ActIdH) { return false; }
            return agent.BB.ReservedSlot?.ContainedBeing == being;
        }

        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.Being.Data.StatsBeing.Toilet.Value > 0.99f) {
                goal.WithWorld(AIVarsH.HasUsedToilet, true);
                return true;
            }
            return false;
        }

        protected override AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            var toiletSpeed = agent.BeingData.StatsBeing.ToiletReliefRate.Value;
            agent.BeingData.StatsBeing.Toilet.AddToBaseWithCompoundLimit(toiletSpeed);
            return InProgress;
        }

        private AIActionUseToilet() : base(ActId, () => T.ActUsetoilet) {
            Preconditions = AIState.With(AIVarsH.HasUsedToilet, false);
            Outcomes = AIState.With(AIVarsH.HasUsedToilet, true);
            WithRequiredNeed(NeedIdH.Toilet);
            WithRequiredAbilities(AbilityIdH.UseToilet, AbilityIdH.Move);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return (agent.Being.Data.StatsBeing.Toilet.Value + 0.001f) * 10f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.ReservedSlot != null) {
                if (agent.BB.ReservedSlot.Parent.IsMakeshift) {
                    if (agent.BeingData.Species.IsHumanoid
                            && agent.BeingData.Mind.Species.IsHumanoid) {
                        ApplyMakeshiftToiletPenalty(agent, ticks);
                    }
                } else {
                    ApplyToiletBonus(agent, ticks);
                }
            } else {
                ApplyNoToiletPenalty(agent, ticks);

            }
            base.OnDeactivate(agent, goal, ticks);
        }
        private void ApplyNoToiletPenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.NoToilet, -15);
        }
        private void ApplyMakeshiftToiletPenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.UsedPlanterToilet, -5);
        }

        private void ApplyToiletBonus(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPerHour, T.UsedAToilet, 3);
        }
    }
}
