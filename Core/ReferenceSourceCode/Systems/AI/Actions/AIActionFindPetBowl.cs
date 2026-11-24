using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionFindPetBowl : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindPetBowl());
        }
        public const string ActId = "FindPetBowl";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindPetBowl() : base(ActId,
                () => T.VerbSearchingFor_V1.F(T.ObjectsFurniturePetbowl)) {
            Outcomes = AIState.With(AIVarsH.TriedFindingPetBowl, true);
            WithRequiredAbilities(AbilityIdH.AnimalEat);
            MarkAsRest();
            MarkAsMobile();
        }

        private void Cleanup(AIAgentBlackboard bb, bool fullCleanup) {
            bb.MoveTargetPos = 0;
            bb.MoveProgress = null;
            bb.ClearBlacklist();

            if (!fullCleanup) { return; }
            // These are transferred to the next AI action, therefore
            // only cleaned on activate
            // the table we're eating at
            bb.UnreserveSlotSide(false);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            Cleanup(agent.BB, true);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            Cleanup(agent.BB, AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal));
        }

        public void IgnorePetBowl(Slot slot, AIAgentComp agent) {
            agent.BB.AddToBlacklist(slot.Parent.EntityId);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.BB.MoveProgress == null) {
                return FindPetBowl(S, agent, goal);
            } else {
                return UsePetBowl(S, agent, goal);
            }
        }

        private AIActionResult FindPetBowl(GameState S, AIAgentComp agent, AIGoal goal) {
            var worker = agent.BeingData;

            Slot slot = null;
            var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
            if (FoodPreference.TryGetPreferredVegan(worker, out var prefersVegan)) {
                slot = S.Sys.Slots.FindClosestMatching(
                    SlotDesignationH.FoodSource, worker, reach,
                    agent.BB.Blacklist, b => !b.Storage.IsEmpty &&
                        FoodPreference.IsPreferred(b.Storage.MatType, prefersVegan),
                    out PetFoodSourceComp _);
            }
            if (slot == null) {
                slot = S.Sys.Slots.FindClosestMatching(
                    SlotDesignationH.FoodSource, worker, reach,
                    agent.BB.Blacklist, b => !b.Storage.IsEmpty,
                    out PetFoodSourceComp _);
            }

            // couldn't find a bowl, we'll look for a raw meal somewhere
            if (slot == null) {
                goal.WithWorld(AIVarsH.TriedFindingPetBowl, true);
                return Success;
            }
            if (agent.BB.TryReserveSlot(slot, goal)) {
                agent.BB.MoveTargetPos = slot.SlotPosIdx;
                if (!agent.Motion.TryGoTo(slot.SlotPosIdx, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    // Unreserved too, or the next search reserves a bowl on top of this one
                    IgnorePetBowl(slot, agent);
                    agent.BB.UnreserveSlotSide(true);
                    agent.BB.ClearMoveTarget();
                }
            } else {
                IgnorePetBowl(slot, agent);
            }
            return InProgress;
        }

        private AIActionResult UsePetBowl(GameState S, AIAgentComp agent, AIGoal goal) {
            // are we at the slot?
            var slot = agent.BB.ReservedSlot;

            // We don't have anything to use, let's just eat
            if (slot == null) {
                D.Err("Unexpected, slot is null! {0}", goal);
                goal.WithWorld(AIVarsH.TriedFindingPetBowl, true);
                return Success;
            }

            var agentPos = agent.Motion.Position.Idx;
            if (agentPos != agent.BB.ReservedSlot.SlotPosIdx) {
                if (!agent.Motion.Progress.HasFailed) {
                    // we're not at the spot!
                    D.Warn("Agent is too far from the eat spot: {0}: {1} | {2}",
                        Pos.StringFromTo(agentPos, agent.BB.ReservedSlot.SlotPosIdx),
                            agent, goal);
                }
                // Let go of this bowl either way, like a table: keeping the reservation made
                // the next search reserve a bowl on top of it
                IgnorePetBowl(slot, agent);
                agent.BB.UnreserveSlotSide(true);
                agent.BB.ClearMoveTarget();
                // find another bowl
                return InProgress;
            }

            var worker = agent.Being;
            worker.Motion.MoveNearbyAndFace(slot.Parent.Tile.PosIdx);

            goal.WithWorld(AIVarsH.IsReadyToEat, true);
            return Success;
        }
    }
}
