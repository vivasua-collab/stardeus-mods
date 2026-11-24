#if !PRODUCTION_BUILD
// #define DEBUG_FIND_FOOD
#endif
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;
using System;
using Game.Systems.Crafting;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public sealed class AIActionFindDrink : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindDrink());
        }
        public const string ActId = "FindDrink";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindDrink() : base(ActId, () => T.VerbSearchingFor_V1.F(T.NounBeverage)) {
            Outcomes = AIState.With(AIVarsH.IsHoldingDrink, true);
            WithRequiredAbilities(AbilityIdH.Eat, AbilityIdH.Carry);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            agent.BB.ClearMoveTarget();
            agent.BB.TargetEntity = null;
            agent.BB.TargetMatType = null;
            SetCurrentMatGroup(agent, MatGroupIdH.Drink);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
            // we should not have any reservations after this
            agent.BB.RemoveActiveReservation(goal);
            if (AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal)) {
                DropDrink(agent);
            }
            agent.BB.TargetMatType = null;
            agent.BB.Int1 = 0;
        }

        private void DropDrink(AIAgentComp agent) {
            agent.BB.TargetEntity = null;
            agent.Being.Graphics.DropIfCarrying();
        }

        private int CurrentDrinkGroup(AIAgentComp agent) {
            return agent.BB.Int1;
        }

        public void SetCurrentMatGroup(AIAgentComp agent, int groupIdH) {
            agent.BB.Int1 = groupIdH;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.BB.MoveProgress == null) {
                // 1. find food location
                // 2. try go to food location
                return FindDrink(S, agent, goal, ticks);
            } else {
                // we are supposed to be near the food now
                // 3. pick up food, if failed, add loc to black list and go to 1.
                return PickUpDrink(S, agent, goal);
            }
        }

        private AIActionResult PickUpDrink(GameState S, AIAgentComp agent, AIGoal goal) {
            var agentPos = agent.Motion.Position.Idx;
            IMatStorage storage;
            bool isPileObj = false;
            if (agent.BB.TargetEntity is Tile t) {
                // fridge or storage device
                storage = t.MaterialStorage;
                // Success!
            } else if (agent.BB.TargetEntity is Obj o) {
                // pile of food items
                storage = o.Pile;
                isPileObj = o.Pile.TreatAsObj;
            } else {
                return Error("Should have TargetEntity Tile or Obj but doesn't: {0}: {1}: {2}",
                    agent.BB.TargetEntity, goal, agent);
            }

            if (storage == null || !storage.Entity.IsActive) {
                return Failed(T.AdRejLackingEdible);
            }

            if (Pos.SqrDistance(agentPos, storage.OutPort) > 2.5f) {
                agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                storage.CancelReservationFor(agent.Being, goal);
                IgnoreStorage(storage, agent);
                agent.BB.MoveProgress = null;
                return InProgress;
            }
            if (isPileObj) {
                storage.RemoveFromInventory();
                if (!agent.Being.Graphics.Carry(storage.Entity)) {
                    storage.AddToInventory();
                    return Failed("Failed to carry food item!");
                }
                agent.BB.TargetEntity = storage.Entity;
            } else {
                var took = storage.Take(agent.BB.TargetMatType, agent.Being, 1, goal);
                if (took == 0) {
                    IgnoreStorage(storage, agent);
                    agent.BB.MoveProgress = null;
                    return InProgress;
                }
                var foodObj = EntityUtils.SpawnRawMaterial(
                    agent.BB.TargetMatType, 1, agentPos, 0f, true, false);

                foodObj.RemoveFromInventory();
                if (!agent.Being.Graphics.Carry(foodObj.Obj, false, false)) {
                    foodObj.ReaddToInventoryIfNeeded();
                    return Failed("Failed to carry food item!");
                }
                agent.BB.TargetEntity = foodObj.Obj;
            }

            // success!
            goal.WithWorld(AIVarsH.IsHoldingDrink, true);
            return Success;
        }

        private void IgnoreStorage(IMatStorage storage, AIAgentComp agent) {
            #if DEBUG_FIND_FOOD
            D.Warn("Ignoring storage: {0}, {1}", storage, agent);
            #endif

            agent.BB.AddToBlacklist(storage.EntityId);
        }

        // FIXME: deduplicate with AIActionFindAnimalFood.FindFood
        private AIActionResult FindDrink(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var i = S.Sys.Inventory;

            int attempts = 20;
            var group = CurrentDrinkGroup(agent);
            var worker = agent.Being;
            // blacklist is null at beginning, this can't work!
            // var ignoredStorages = agent.BB.Blacklist;
            // we have to have nearby so a food item placed on top of an oxy pump will be reachable
            var flags = PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination;

            // TODO: addicts could risk their safety to get to the item
            bool safeOnly = true;

            int budgetSkips = 0;
            while (attempts-- > 0) {
                var storage = i.FindClosestReachableStorageByGroup(
                        group, worker, agent.BB.Blacklist, null, safeOnly, false);

                #if DEBUG_FIND_FOOD
                if (attempts < 18) {
                    D.Warn("Find drink attempts left #{0}: {1}, {2}, Ign: {3}, {4}",
                        attempts, group, storage, agent.BB.Blacklist?.Count, agent);
                }
                #endif

                if (storage != null) {
                    var drinkOptions = Caches.MatList;
                    drinkOptions.Clear();
                    if (storage.AvailableForGroup(group, drinkOptions)) {
                        while (drinkOptions.Count > 0) {
                            var food = S.Rng.From(drinkOptions);
                            drinkOptions.Remove(food);
                            if (TryReserve(storage, food.Type, agent, goal)) {
                                // Success, we can move on
                                agent.BB.MoveTargetPos = storage.OutPort;
                                if (!agent.Motion.TryGoTo(storage.OutPort,
                                        flags, out agent.BB.MoveProgress)) {
                                    storage.CancelReservationFor(agent.Being, goal);
                                    IgnoreStorage(storage, agent);
                                    agent.BB.MoveProgress = null;
                                    break; // while
                                }
                                return InProgress;
                            } else {
                                D.ErrSoft("Failed to reserve even though food was available in storage! {0} -> {1}",
                                    storage, food);
                            }
                        }
                    }
                    IgnoreStorage(storage, agent);
                } else {
                    // we tried looking for raw food and didn't find any
                    if (TryBrewing(S, agent)) {
                        DropDrink(agent);
                        return PutBack("Will do cooking first!");
                    } else {
                        if (agent.BeingData.HasTrait(TraitIdH.CoffeeAddict)) {
                            AddMentalEffect(agent, ticks, Consts.TicksPer3Hours,
                                TraitDef.MoodReason(T.NoCoffee, TraitIdH.CoffeeAddict), -5);
                        }
                        return Failed(T.AdRejLackingDrink);
                    }
                }

                // Can we still loop?
                if (!GameTimer.HasAvailableFrameBudget()) {
                    budgetSkips++;
                    // We're out of time budget, let's let other code run for a while
                    return InProgress;
                }
            }

            D.ErrSoft("Failed locating drink in 20 attempts! Skipped due to frame budget: {0}. {1} -> {2}.",
                budgetSkips, goal, agent);
            return Failed(T.AdRejLackingDrink);
        }

        private bool TryHaulingDrinkIngredients(GameState S, AIAgentComp agent) {
            if (!agent.Jobs.CanDo(JobTypeIdH.Hauling)) {
                return false;
            }
            var brewingDevice = S.Components.FindClosestMatching<MultiCrafterComp>(
                agent.Motion.Position.Exact,
                agent.Motion.IsGrounded, (c) => {
                    // FIXME if new types of drinks are added, this may not work
                    if (c.Settings.CompatibleTypes?.Contains(MatGroupId.Drink) == true) {
                        var g = c.Entity.Goal;
                        if (AIGoal.IsAvailable(g) && g.JobIdH == JobTypeIdH.Hauling) {
                            return true;
                        }
                    }
                    return false;
                });
            if (brewingDevice != null) {
                var g = brewingDevice.Entity.Goal;
                if (g == null) {
                    D.Err("TryHaulingDrinkIngredients return a brewing device with null goal! {0}",
                        brewingDevice);
                } else {
                    if (g.TrySetAgent(agent)) {
                        g.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks);
                        D.Warn("Pre-assigning hauling ingredients goal while drink was not found: {0} => {1}",
                            brewingDevice, agent);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryBrewing(GameState S, AIAgentComp agent) {
            Caches.Goals.Clear();

            if (!agent.Jobs.CanDo(JobTypeIdH.Cooking)) {
                return TryHaulingDrinkIngredients(S, agent);
            }

            if (!S.Query.AIGoalsByType.Ask(JobTypeIdH.Cooking, Caches.Goals)) {
                // no cooking goals, but maybe there are hauling goals for cooking
                // equipment?!
                return TryHaulingDrinkIngredients(S, agent);
            }
            // try to assign
            AIGoal closestAvailable = null;
            float distToGoal = float.MaxValue;
            var wPos = agent.Motion.Position.Idx;
            var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
            for (int i = Caches.Goals.Count - 1; i >= 0; i--) {
                AIGoal g = Caches.Goals[i];
                if (AIGoal.IsAvailable(g.State)) {
                    // skip stations this being can't walk to
                    var gPos = g.Target.Entity.PosIdx;
                    if (!reach.CanReach(g.Target.Entity)) {
                        continue;
                    }
                    var dist = Pos.SqrDistance(wPos, gPos);
                    if (dist < distToGoal) {
                        distToGoal = dist;
                        closestAvailable = g;
                        // close enough
                        if (distToGoal < 10 * 10) {
                            break;
                        }
                    }
                }
            }
            if (closestAvailable != null) {
                if (closestAvailable.TrySetAgent(agent)) {
                    closestAvailable.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks);
                    // D.Warn("Pre-assigning cooking goal while drink was not found: {0} => {1}",
                    //     closestAvailable, agent);
                    return true;
                }
            }
            return false;
        }

        private bool TryReserve(IMatStorage storage, MatType mealType,
                AIAgentComp agent, AIGoal goal) {
            var reserved = storage.Reserve(mealType, agent.Being, 1, goal);
            if (reserved == 0) {
                D.Warn("Did not reserve anything!");
                return false;
            }

            agent.BB.TargetEntity = storage.Entity;
            agent.BB.TargetMatType = mealType;
            return true;
        }
    }
}
