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
using Game.Achievements;
using Game.Systems.Drama;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public sealed class AIActionConsumeDrink : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConsumeDrink());
        }
        public const string ActId = "ConsumeDrink";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.Create(
                SocialContext.Drink, expectedDuration: Consts.TicksPer30Minutes);
        private AIActionConsumeDrink() : base(ActId, () => T.ActEat) {
            Preconditions = AIState
                .With(AIVarsH.IsThirsty, true)
                .And(AIVarsH.IsReadyToDrink, true);
            Outcomes = AIState.With(AIVarsH.IsThirsty, false);
            WithRequiredAbilities(AbilityIdH.Eat);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // table
            // agent.BB.ReservedSlot
            // drink
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

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var stats = agent.BeingData.StatsBeing;
            if (agent.BB.TargetEntity == null) {
                D.Err("Target entity is null in consume food action! {0} -> {1}, {2}",
                    D.DumpInstance(agent.BB), agent, goal);
                return Failed(T.AdRejLackingDrink);
            }
            if (agent.BB.TargetEntity.TryGetComponent<UnstoredMatComp>(out var mat)) {
                if (mat.IsAddedToInventory) {
                    // so nobody would snatch it after save / load
                    mat.RemoveFromInventory();
                }
            } else {
                D.Err("Drank a beverage but target entity is not what we expect! {0}",
                    agent.BB.TargetEntity);
                return Error("Drink has no UnstoredMatComp");
            }

            var prog = agent.TicksInCurrentAction / (float) Consts.TicksPer30Minutes;
            if (prog < 1f) {
                stats.Sleep.TryIncrease(stats.SleepRate.Value);
                Animate(agent, prog);
                return InProgress;
            }

            if (agent.Being.Graphics.IsCarrying()) {
                agent.Being.Graphics.Drop(agent.BB.TargetEntity,
                    skipInventoryCheck: true);
            }
            int took = mat.Take(mat.Type, null, 1, goal);
            if (took > 0) {
                if (agent.BeingData.Mind.Health != null) {
                    The.AchievementTracker.Unlock(AchievementFirstCup.AchievementId);
                    var isAddict = agent.BeingData.HasTrait(TraitIdH.CoffeeAddict);
                    int effect = isAddict ? 6 : 3;
                    var reason = isAddict
                        ? TraitDef.MoodReason(mat.Type.NameT, TraitIdH.CoffeeAddict)
                        : mat.Type.NameT;
                    AddMentalEffect(agent, ticks, Consts.TicksPer3Hours, reason, effect);
                }
            } else {
                D.Err("Wat, where's the coffee? {0} | {1} | {2}", mat, agent, goal);
            }

            TryWalkAway(agent);

            goal.WithWorld(AIVarsH.IsThirsty, false);
            return Success;
        }

        private void Animate(AIAgentComp agent, float progress) {
            agent.ShowInfoBar(Consts.ColorFuelBar, progress, T.ActDrink);
            if (agent.BB.Float1NotSaved == 0f) {
                var animInTime = Rng.URange(0.09f, 1.5f);
                var animOutTime = Rng.URange(0.09f, 1.5f);
                agent.BB.Float1NotSaved = animInTime;
                agent.BB.Float2NotSaved = animOutTime;
            }
            var punchTarget = agent.BB.TargetEntity.Position;
            agent.Being.Graphics.Punch(
                punchTarget - agent.Motion.Position.Exact, 0.05f,
                    agent.BB.Float1NotSaved,
                    agent.BB.Float2NotSaved);
        }
    }
}
