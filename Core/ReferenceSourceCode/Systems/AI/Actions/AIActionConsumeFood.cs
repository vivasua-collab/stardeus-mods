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
using Game.Systems.Conditions;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionConsumeFood : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConsumeFood());
        }
        private Curve infectCurve;
        public const string ActId = "ConsumeFood";
        public override SocialChatContext ChatContext => SocialChatContext.Create(
                SocialContext.Meal,
                faceBuddy: false,
                expectedDuration: Consts.TicksPerHour);
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionConsumeFood() : base(ActId, () => T.ActEat) {
            Preconditions = AIState
                .With(AIVarsH.IsHungry, true)
                .And(AIVarsH.IsReadyToEat, true);
            Outcomes = AIState.With(AIVarsH.IsHungry, false);
            WithRequiredAbilities(AbilityIdH.Chew, AbilityIdH.Eat);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // table
            // agent.BB.ReservedSlot
            // meal
            // agent.BB.TargetEntity
            // punch animation in time
            agent.BB.Float1NotSaved = 0f;
            // punch animation out time
            agent.BB.Float2NotSaved = 0f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.UnreserveSlotSide(false);
            agent.BB.Float1NotSaved = 0f;
            agent.BB.Float2NotSaved = 0f;
            if (agent.BB.TargetEntity != null) {
                if (agent.BB.TargetEntity.TryGetComponent<UnstoredMatComp>(
                        out var pile)) {
                    pile.ReaddToInventoryIfNeeded();
                }
                agent.BB.TargetEntity = null;
            }
            agent.BB.RemoveOpportunity(agent.S);
            agent.Being.Graphics.DropIfCarrying();
            agent.Being.Graphics.SetOffset(Consts.V2Zero);
            agent.Being.Graphics.SetRotation(0f, true);
            agent.HideInfoBar();
        }
        private void ApplyNoTablePenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.AteWithoutTable, -5);
        }

        private void ApplyRawFoodPenalty(AIAgentComp agent, long ticks) {
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.AteRawFood, -15);
        }

        private void ApplyTableBonus(AIAgentComp agent, long ticks, bool IsMakeshift) {
            if (IsMakeshift) {
                AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.AteAtMakeshiftTable, -3);
            } else {
                AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, T.AteAtTable, 3);
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.TargetEntity == null) {
                D.Err("Target entity is null in consume food action! {0} -> {1}, {2}",
                    D.DumpInstance(agent.BB), agent, goal);
                return Failed(T.AdRejLackingEdible);
            }
            Being eater = agent.Being;
            AIAgentComp feeder = agent;
            Vector2 animatePunchTarget = feeder.BB.TargetEntity.Position;

            if (!Feed(S, goal, eater, feeder, animatePunchTarget,
                    out bool isRawFood, out var foodType)) {
                return InProgress;
            }

            FinishEating(agent, goal, ticks, isRawFood, foodType);

            goal.WithWorld(AIVarsH.IsHungry, false);
            return Success;
        }

        public bool Feed(GameState S, AIGoal goal,
                Being eater, AIAgentComp feeder, Vector2 animatePunchTarget,
                out bool isRawFood, out MatType foodType) {
            var stats = eater.Data.StatsBeing;
            var eatRate = stats.HungerSatisfyRate.Value;
            var stomach = stats.StomachContents;
            var hunger = stats.Hunger;
            var isFinishedEating = false;
            EdibleComp edible;
            if (feeder.BB.TargetEntity.TryGetComponent(out edible)) {
                var pile = edible.Obj.Pile;
                foodType = pile.Type;
                isRawFood = Arrays.Contains(pile.Groups, MatGroupIdH.RawFood);
                if (!edible.TryConsume(eatRate, feeder.Being, goal)) {
                    isFinishedEating = true;
                }
            } else {
                foodType = null;
                isRawFood = false;
                // target entity is not an edible
                D.Err("Target entity does not have an EdibleComp in eat action: {0}, {1}",
                    feeder.BB.TargetEntity, goal);
            }

            var randomPoisonFactor = Rng2.Range(0.5f, 1f, edible.EntityId, Primes.Prime021);
            if (isRawFood || randomPoisonFactor > 0.99f) {
                infectCurve ??= Tunables.DiseaseTransmissionSpeedByAgeCurve;
                var rate = infectCurve.Evaluate(eater.Data.Body.AgePercent) * randomPoisonFactor;
                // D.Warn("Fluc: {0}, Rate: {1}", randomPoisonFactor, rate);
                // one hour == one encounter
                rate /= Consts.TicksPerHour;
                Condition.Advance(S, G.Ticks, ConditionIdH.FoodPoisoning, eater.Data, 1, rate,
                    0f, false, Severity.Critical);
            }

            hunger.TryIncrease(eatRate);

            // stomach contents will go through intestines, etc, but
            // we want to also add a bit of satiety feeling to the hunger bar
            // or the feedback loop will be too slow to notice that
            // it is time to stop eating
            if (stomach.TryIncrease(eatRate)) {
                if (!isFinishedEating && eater == feeder.Being) {
                    var hungerVal = AINeedHunger.CombinedHungerFor(stats);
                    Animate(eater.AIAgent, hungerVal, animatePunchTarget);
                }
            } else {
                isFinishedEating = true;
                // TODO Throw up, mood debuff, guilt for overeating
            }

            if (!isFinishedEating) {
                // still has remaining food
                if (edible != null && edible.Remaining < 0.2f) {
                    // last bite
                    edible.TryConsume(0.2f, feeder.Being, goal);
                }
            }
            return isFinishedEating;
        }

        private void FinishEating(AIAgentComp agent, AIGoal goal, long ticks,
                bool isRawFood, MatType foodType) {
            bool isTable;
            bool isMakeshiftTable;
            if (agent.BB.ReservedSlot == null) {
                isTable = false;
                isMakeshiftTable = false;
            } else {
                isTable = true;
                isMakeshiftTable = agent.BB.ReservedSlot.Parent.IsMakeshift;
            }

            TryWalkAway(agent);
            if (isTable) {
                ApplyTableBonus(agent, ticks, isMakeshiftTable);
            } else {
                ApplyNoTablePenalty(agent, ticks);
            }
            if (isRawFood) {
                ApplyRawFoodPenalty(agent, ticks);
            }
            FoodPreference.ApplyMentalEffect(agent, foodType, ticks);
        }

        private void Animate(AIAgentComp agent, float hunger, Vector2 target) {
            agent.ShowInfoBar(Consts.ColorFuelBar, hunger, T.Eating);
            if (agent.BB.Float1NotSaved == 0f) {
                var animInTime = Rng.URange(0.09f, 1.5f);
                var animOutTime = Rng.URange(0.09f, 1.5f);
                agent.BB.Float1NotSaved = animInTime;
                agent.BB.Float2NotSaved = animOutTime;
            }
            var punchTarget = target;
            agent.Being.Graphics.Punch(
                punchTarget - agent.Motion.Position.Exact, 0.05f,
                    agent.BB.Float1NotSaved,
                    agent.BB.Float2NotSaved);
        }
    }
}
