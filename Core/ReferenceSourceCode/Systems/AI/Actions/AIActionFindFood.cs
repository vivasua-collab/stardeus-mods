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

namespace Game.Systems.AI {
    public sealed class AIActionFindFood : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindFood());
        }
        public const string ActId = "FindFood";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindFood() : base(ActId, () => T.VerbSearchingFor_V1.F(T.Food)) {
            Outcomes = AIState.With(AIVarsH.IsHoldingFood, true);
            WithRequiredAbilities(AbilityIdH.Eat, AbilityIdH.Carry);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
            agent.BB.TargetEntity = null;
            agent.BB.TargetMatType = null;
            SetCurrentFoodGroup(agent, MatGroupIdH.Food);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearBlacklist();
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
            // we should not have any reservations after this
            agent.BB.RemoveActiveReservation(goal);
            if (AIGoal.IsFailed(goal) || AIGoal.IsCancelled(goal)) {
                DropFood(agent);
            }
            agent.BB.TargetMatType = null;
            agent.BB.Int1 = 0;
        }

        private void DropFood(AIAgentComp agent) {
            agent.BB.TargetEntity = null;
            agent.Being.Graphics.DropIfCarrying();
        }

        private int CurrentFoodGroup(AIAgentComp agent) {
            return agent.BB.Int1;
        }

        public void SetCurrentFoodGroup(AIAgentComp agent, int groupIdH) {
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
                return FindFood(S, agent, goal, ticks);
            } else {
                // we are supposed to be near the food now
                // 3. pick up food, if failed, add loc to black list and go to 1.
                return PickUpFood(S, agent, goal);
            }
        }

        private AIActionResult PickUpFood(GameState S, AIAgentComp agent, AIGoal goal) {
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
            goal.WithWorld(AIVarsH.IsHoldingFood, true);
            return Success;
        }

        private void IgnoreStorage(IMatStorage storage, AIAgentComp agent) {
            #if DEBUG_FIND_FOOD
            D.Warn("Ignoring storage: {0}, {1}", storage, agent);
            #endif

            agent.BB.AddToBlacklist(storage.EntityId);
        }

        // FIXME: deduplicate with AIActionFindAnimalFood.FindFood
        private AIActionResult FindFood(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var i = S.Sys.Inventory;

            int attempts = 20;
            var group = CurrentFoodGroup(agent);
            var worker = agent.Being;
            // blacklist is null at beginning, this can't work!
            // var ignoredStorages = agent.BB.Blacklist;
            // we have to have nearby so a food item placed on top of an oxy pump will be reachable
            var flags = PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination;
            bool safeOnly = true;
            // starving colonists will try to get food even if they can die doing it
            if (agent.BeingData.HasCondition(ConditionIdH.Starvation)
                    && agent.BeingData.StatsBeing.Hunger.Value <= 0.001f) {
                flags |= PathReqFlags.UnsafeOK;
                safeOnly = false;
            }
            int budgetSkips = 0;
            var hasFoodPreference = FoodPreference.TryGetPreferredVegan(
                agent.BeingData, out var prefersVegan);
            while (attempts-- > 0) {
                IMatStorage storage = null;
                bool preferredOnly = false;
                if (hasFoodPreference) {
                    storage = i.FindClosestReachableStorageByGroup(
                        group, worker, agent.BB.Blacklist,
                        s => HalfConsumedFood(s) && FoodPreference.StorageHasPreferredFood(
                            s, group, prefersVegan), safeOnly, false);
                    if (storage == null) {
                        storage = i.FindClosestReachableStorageByGroup(
                            group, worker, agent.BB.Blacklist,
                            s => FoodPreference.StorageHasPreferredFood(
                                s, group, prefersVegan), safeOnly, false);
                    }
                    preferredOnly = storage != null;
                }
                if (storage == null) {
                    storage = i.FindClosestReachableStorageByGroup(
                        group, worker, agent.BB.Blacklist, HalfConsumedFood, safeOnly, false);
                }
                if (storage == null) {
                    storage = i.FindClosestReachableStorageByGroup(
                        group, worker, agent.BB.Blacklist, null, safeOnly, false);
                }

                #if DEBUG_FIND_FOOD
                if (attempts < 18) {
                    D.Warn("Find food attempts left #{0}: {1}, {2}, Ign: {3}, {4}",
                        attempts, group, storage, agent.BB.Blacklist?.Count, agent);
                }
                #endif

                if (storage != null) {
                    var foodOptions = Caches.MatList;
                    foodOptions.Clear();
                    if (storage.AvailableForGroup(group, foodOptions)) {
                        if (preferredOnly) {
                            FoodPreference.KeepPreferredFood(foodOptions, prefersVegan);
                        }
                        while (foodOptions.Count > 0) {
                            var food = S.Rng.From(foodOptions);
                            foodOptions.Remove(food);
                            if (TryReserve(storage, food.Type, agent, goal)) {
                                // Success, we can move on
                                agent.BB.MoveTargetPos = storage.OutPort;
                                if (!agent.Motion.TryGoTo(storage.OutPort,
                                        flags, out agent.BB.MoveProgress)) {
                                    storage.CancelReservationFor(agent.Being, goal);
                                    IgnoreStorage(storage, agent);
                                    agent.BB.MoveProgress = null;
                                    break; // while
                                } else if (!safeOnly && !agent.BeingData.Safety.IsActuallySafeAt(storage.OutPort)) {
                                    var msg = Texts.Yellow(T.BeingRiskingLifeForFood_V2.F(
                                        agent.BeingData.Mind.LinkToShortName,
                                            Texts.LinkTo(food.Type.NameT, storage.OutPort)));
                                    S.Sig.ShowFanfare.Send(msg);
                                    S.Sys.Log.AddLine(msg);
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
                    if (group == MatGroupIdH.Food) {
                        var willEatRawFood = agent.BeingData.StatsBeing.Hunger.Value < 0.1f;
                        if (willEatRawFood) {
                            // try raw food
                            SetCurrentFoodGroup(agent, MatGroupIdH.RawFood);
                            agent.BB.MoveProgress = null;
                            return InProgress;
                        }
                    }

                    // we tried looking for raw food and didn't find any
                    if (TryCooking(S, agent)) {
                        DropFood(agent);
                    }
                    AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.Hungry, -10);
                    return Failed(T.AdRejLackingEdible);
                }

                // Can we still loop?
                if (!GameTimer.HasAvailableFrameBudget()) {
                    budgetSkips++;
                    // We're out of time budget, let's let other code run for a while
                    return InProgress;
                }
            }

            D.ErrSoft("Failed locating food in 20 attempts! Skipped due to frame budget: {0}. {1} -> {2}.",
                budgetSkips, goal, agent);
            return Failed(T.AdRejLackingEdible);
        }

        private bool HalfConsumedFood(IMatStorage storage) {
            if (storage is UnstoredMatComp pile && pile.StackSize == 1) {
                if (pile.Obj.TryGetComponent<EdibleComp>(out var edible)) {
                    return edible.Nutrition > edible.Remaining;
                }
            }
            return false;
        }

        private bool TryHaulingCookingIngredients(GameState S, AIAgentComp agent) {
            if (!agent.Jobs.CanDo(JobTypeIdH.Hauling)) {
                return false;
            }
            var station = S.Components.FindClosestMatching<MultiCrafterComp>(
                agent.Motion.Position.Exact,
                agent.Motion.IsGrounded, (c) => {
                    if (c.Settings.CompatibleTypes?.Contains(MatGroupId.Food) == true) {
                        var g = c.Entity.Goal;
                        if (AIGoal.IsAvailable(g) && g.JobIdH == JobTypeIdH.Hauling) {
                            return true;
                        }
                    }
                    return false;
                });
            if (station != null) {
                var g = station.Entity.Goal;
                if (g == null) {
                    D.Err("TryHaulingCookingIngredients return a station with null goal! {0}",
                        station);
                } else {
                    if (g.TrySetAgent(agent)) {
                        g.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks);
                        // D.Warn("Pre-assigning hauling ingredients goal while food was not found: {0} => {1}",
                        //     station, agent);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryCooking(GameState S, AIAgentComp agent) {
            Caches.Goals.Clear();

            if (!agent.Jobs.CanDo(JobTypeIdH.Cooking)) {
                return TryHaulingCookingIngredients(S, agent);
            }

            if (!S.Query.AIGoalsByType.Ask(JobTypeIdH.Cooking, Caches.Goals)) {
                // no cooking goals, but maybe there are hauling goals for cooking
                // equipment?!
                return TryHaulingCookingIngredients(S, agent);
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
                    D.Warn("Pre-assigning cooking goal while food was not found: {0} => {1}",
                        closestAvailable, agent);
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
