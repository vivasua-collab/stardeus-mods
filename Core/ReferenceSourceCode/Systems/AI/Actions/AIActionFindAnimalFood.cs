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

namespace Game.Systems.AI {
    public sealed class AIActionFindAnimalFood : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindAnimalFood());
        }
        public const string ActId = "FindAnimalFood";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFindAnimalFood() : base(ActId, () => T.VerbSearchingFor_V1.F(T.Food)) {
            Preconditions = AIState.With(AIVarsH.TriedFindingPetBowl, true);
            Outcomes = AIState.With(AIVarsH.IsReadyToEat, true);
            WithRequiredAbilities(AbilityIdH.AnimalEat);
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
                agent.BB.UnreserveSlot(false, false);
                // we don't want to pass the target entity
                agent.BB.TargetEntity = null;
            }
            agent.BB.TargetMatType = null;
            agent.BB.Int1 = 0;
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
                if (agent.BB.ReservedSlot != null) {
                    // we have a pet bowl
                    goal.WithWorld(AIVarsH.IsReadyToEat, true);
                    return Success;
                }
                // 1. find food location
                // 2. try go to food location
                return FindFood(S, agent, goal, ticks);
            } else {
                // we are supposed to be near the food now
                // 3. pick up food, if failed, add loc to black list and go to 1.
                return ApproachFood(S, agent, goal);
            }
        }

        private AIActionResult ApproachFood(GameState S, AIAgentComp agent, AIGoal goal) {
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
                return Failed(T.RequiredObjectIsNotAvailable);
            }

            if (Pos.SqrDistance(agentPos, storage.OutPort) > 2.5f) {
                agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                storage.CancelReservationFor(agent.Being, goal);
                IgnoreStorage(storage, agent);
                agent.BB.MoveProgress = null;
                return InProgress;
            }
            if (isPileObj) {
                agent.BB.TargetEntity = storage.Entity;
                storage.RemoveFromInventory();
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
                agent.BB.TargetEntity = foodObj.Obj;
                // D.Warn("target animal food: {0} | {1}", foodObj.Obj.Pile, goal);
            }
            // success!
            goal.WithWorld(AIVarsH.IsReadyToEat, true);
            return Success;
        }

        private void IgnoreStorage(IMatStorage storage, AIAgentComp agent) {
            #if DEBUG_FIND_FOOD
            D.Warn("Ignoring storage (animal): {0}, {1}", storage, agent);
            #endif
            agent.BB.AddToBlacklist(storage.EntityId);
        }

        // FIXME: deduplicate with AIActionFindFood.FindFood
        private AIActionResult FindFood(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var i = S.Sys.Inventory;

            int attempts = 20;
            var group = CurrentFoodGroup(agent);
            var worker = agent.Being;
            // this will be null at start!
            // var ignoredStorages = agent.BB.Blacklist;
            // we have to have nearby so a food item placed on top of an oxy pump will be reachable
            var flags = PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination;
            bool safeOnly = true;
            if (agent.BeingData.StatsBeing.Hunger.Value < 0.001f) {
                // ignore safety to not die from starvation
                flags |= PathReqFlags.UnsafeOK;
                safeOnly = false;
            }
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
                        // try raw food
                        SetCurrentFoodGroup(agent, MatGroupIdH.RawFood);
                        agent.BB.MoveProgress = null;
                        return InProgress;
                    }

                    AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.Hungry, -10);
                    return Failed(T.AdRejLackingEdible);
                }

                // Can we still loop?
                if (!GameTimer.HasAvailableFrameBudget()) {
                    // We're out of time budget, let's let other code run for a while
                    return InProgress;
                }
            }

            D.ErrSoft("Failed locating food in 20 attempts! {0} -> {1}", goal, agent);
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

        private bool TryReserve(IMatStorage storage, MatType mealType,
                AIAgentComp agent, AIGoal goal) {
            var reserved = storage.Reserve(mealType, agent.Being, 1, goal);
            if (reserved == 0) {
                return false;
            }

            agent.BB.TargetEntity = storage.Entity;
            agent.BB.TargetMatType = mealType;
            return true;
        }
    }
}
