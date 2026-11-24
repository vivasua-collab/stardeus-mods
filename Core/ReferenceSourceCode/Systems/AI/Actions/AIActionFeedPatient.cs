using Game.Components;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using KL.Grid;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class AIActionFeedPatient : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFeedPatient());
        }
        public const string ActId = "FeedPatient";
        private AIActionConsumeFood eatAct;
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionFeedPatient() : base(ActId, () => T.ActFeedingPatient) {
            Preconditions = AIState.With(AIVarsH.IsHoldingFood, true)
                .And(AIVarsH.IsPatientFed, false);
            Outcomes = AIState.With(AIVarsH.IsPatientFed, true);
            WithRequiredAbilities(AbilityIdH.UseItem, AbilityIdH.Manipulate);
            WithRequiredJobType(JobTypeIdH.Medical);

            MarkAsWork();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {

            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            Being target = agent.BB.TargetBeing;
            if (target == null) {
                if (goal.Target.Entity is not Being b) {
                    D.Err("Target entity not being for feed patient goal! {0} -> {1}",
                        goal, goal.Target.Entity);
                    return Error("Wrong target");
                }
                target = agent.BB.TargetBeing = b;
                // if we're here, there is no inventory, we are likely checking
                // for disposal
                if (target == null || target.IsDead
                        || !AINeedHunger.NeedsSpoonFeeding(target.Data)) {
                    goal.WithWorld(AIVarsH.IsPatientFed, true);
                    return Success;
                }
                if (!Pos.AreAdjacentNonDiagonal(target.PosIdx, agent.PosIdx)) {
                    if (!TryGoToOnce(agent, target, true, Path.PathReqFlags.NonDiagonal, tryOnTop: false)) {
                        return FailedNavToTarget(agent, target.PosIdx);
                    }
                }
                if (target.IsAlive && !target.Motion.IsImmobilized) {
                    target.Motion.Immobilize(ImmobilizeReasonIdH.Operation, true);
                }
                return InProgress;
            }

            if (!Pos.AreAdjacent(target.PosIdx, agent.PosIdx)) {
                return FailedNavToTarget(agent, target.PosIdx);
            }

            FacePosition(agent, target.Position);
            if (Vectors.SqrDistance(target.Graphics.Position, agent.Being.Graphics.Position) > 0.9f * 0.9f) {
                var dir = target.Graphics.Position - agent.Being.Graphics.Position;
                dir = dir.normalized * 0.75f;
                agent.Being.Motion.TeleportTo(target.Graphics.Position - dir);
            }

            eatAct ??= (AIActionConsumeFood) Registries.AIActions.FastGet(
                AIActionConsumeFood.ActIdH);

            if (!eatAct.Feed(S, goal, target, agent, Consts.V2Zero,
                    out bool isRawFood, out var foodType)) {
                return InProgress;
            }

            FoodPreference.ApplyMentalEffect(target.AIAgent, foodType, ticks);
            goal.WithWorld(AIVarsH.IsPatientFed, true);
            return Success;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // punch animation in time
            agent.BB.Float1NotSaved = 0f;
            // punch animation out time
            agent.BB.Float2NotSaved = 0f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.TargetBeing != null) {
                if (agent.BB.TargetBeing.Motion.IsImmobilized) {
                    agent.BB.TargetBeing.Motion.Immobilize(
                        ImmobilizeReasonIdH.Operation, false);
                }
                agent.BB.TargetBeing = null;
            }
            // food
            if (agent.BB.TargetEntity != null) {
                if (agent.BB.TargetEntity.TryGetComponent<UnstoredMatComp>(
                        out var pile)) {
                    pile.ReaddToInventoryIfNeeded();
                }
                agent.BB.TargetEntity = null;
            }
            agent.Being.Graphics.DropIfCarrying();
            agent.Being.Graphics.SetOffset(Consts.V2Zero);
            agent.Being.Graphics.SetRotation(0f, true);
            agent.BB.ClearMoveTarget();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
        }
    }
}
