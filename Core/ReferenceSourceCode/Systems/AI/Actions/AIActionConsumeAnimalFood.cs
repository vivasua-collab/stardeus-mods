using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionConsumeAnimalFood : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConsumeAnimalFood());
        }
        public const string ActId = "ConsumeAnimalFood";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionConsumeAnimalFood() : base(ActId, () => T.ActAnimaleat) {
            Preconditions = AIState
                .With(AIVarsH.IsHungry, true)
                .And(AIVarsH.IsReadyToEat, true);
            Outcomes = AIState.With(AIVarsH.IsHungry, false);
            WithRequiredAbilities(AbilityIdH.Chew, AbilityIdH.AnimalEat);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // bowl
            // agent.BB.ReservedSlot
            // meal (if bowl is missing)
            // agent.BB.TargetEntity
            // punch animation in time
            agent.BB.Float1NotSaved = 0f;
            // punch animation out time
            agent.BB.Float2NotSaved = 0f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.UnreserveSlot(addToIgnoreList: false, keepUsing: false);
            agent.BB.Float1NotSaved = 0f;
            agent.BB.Float2NotSaved = 0f;
            if (agent.BB.TargetEntity != null) {
                if (agent.BB.TargetEntity.TryGetComponent<UnstoredMatComp>(
                        out var pile)) {
                    pile.ReaddToInventoryIfNeeded();
                }
                agent.BB.TargetEntity = null;
            }
            agent.HideInfoBar();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var stats = agent.BeingData.StatsBeing;
            var eatRate = stats.HungerSatisfyRate.Value;
            if (!agent.IsFeelingSafeNow) {
                eatRate *= 2f;
            }
            var stomach = stats.StomachContents;
            var hunger = stats.Hunger;

            bool isFinishedEating = false;
            MatType foodType = null;
            EdibleComp edible = null;
            if (agent.BB.TargetEntity != null) {
                if (agent.BB.TargetEntity.TryGetComponent(out edible)) {
                    foodType = edible.Obj.Pile.Type;
                    if (!edible.TryConsume(eatRate, agent.Being, goal)) {
                        isFinishedEating = true;
                    }
                } else {
                    // target entity is not an edible
                    D.Err("Target entity does not have an EdibleComp in animal eat action: {0}, {1}",
                        agent.BB.TargetEntity, goal);
                    }
            }

            if (hunger.TryIncrease(eatRate / stats.IntestinesContents.CompoundLimit)) {
                bool hasFood = true;
                if (agent.BB.ReservedSlot != null) {
                    if (agent.BB.ReservedSlot.Parent is PetFoodSourceComp bowl) {
                        foodType = bowl.Storage.MatType;
                        hasFood = bowl.Consume(eatRate);
                    }
                }
                // stomach contents will go through intestines, etc, but
                // we want to also add a bit of satiety feeling to the hunger bar
                // or the feedback loop will be too slow to notice that
                // it is time to stop eating
                if (hasFood) {
                    if (stomach.TryIncrease(eatRate)) {
                        if (!isFinishedEating) {
                            var hungerVal = AINeedHunger.CombinedHungerFor(stats);
                            Animate(agent, hungerVal);
                            return InProgress;
                        }
                    } else {
                        // TODO Throw up, mood debuff, guilt for overeating
                    }
                }
            }

            if (!isFinishedEating) {
                // still has remaining food
                if (edible != null && edible.Remaining < 0.2f) {
                    D.Warn("Consuming last bite of food: {0}", edible);
                    // last bite
                    edible.TryConsume(0.2f, agent.Being, goal);
                }
            }

            FinishEating(agent, goal, ticks, foodType);

            goal.WithWorld(AIVarsH.IsHungry, false);
            return Success;
        }

        private void FinishEating(AIAgentComp agent, AIGoal goal, long ticks,
                MatType foodType) {
            if (agent.BB.ReservedSlot != null) {
                ApplyBowlBonus(agent, ticks);
            } else {
                ApplyNoBowlPenalty(agent, ticks);
            }
            FoodPreference.ApplyMentalEffect(agent, foodType, ticks);
            TryWalkAway(agent);
        }

        private void ApplyBowlBonus(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer2Hours, T.AteFromABowl, 3);
        }
        private void ApplyNoBowlPenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer2Hours, T.AteWithoutBowl, -5);
        }

        private void Animate(AIAgentComp agent, float hunger) {
            var punchTarget = agent.BB.TargetEntity ?? agent.BB.ReservedSlot?.Parent.Tile;
            if (punchTarget == null) {
                return;
            }
            var bar = agent.ShowInfoBar(Consts.ColorFuelBar, hunger, hunger, 1,
                T.Eating);
            bar.IsPretranslated = true;
            if (agent.BB.Float1NotSaved == 0f) {
                var animInTime = Rng.URange(0.09f, 1.5f);
                var animOutTime = Rng.URange(0.09f, 1.5f);
                agent.BB.Float1NotSaved = animInTime;
                agent.BB.Float2NotSaved = animOutTime;
            }
            agent.Being.Graphics.Punch(
                punchTarget.Position - agent.Motion.Position.Exact, 0.05f,
                    agent.BB.Float1NotSaved,
                    agent.BB.Float2NotSaved);
        }
    }
}
