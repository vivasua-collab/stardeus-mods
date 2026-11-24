using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.Path;
using KL.Grid;

namespace Game.Systems.AI {
    public sealed class AIActionEnterShuttle : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionEnterShuttle());
        }
        public const string ActId = "EnterShuttle";
        public static readonly int ActIdH = Hashes.S(ActId);
        private AIActionEnterShuttle() : base(ActId, () => T.ActEntershuttle) {
            Preconditions = AIState.With(AIVarsH.IsInShuttle, false);
            Outcomes = AIState.With(AIVarsH.IsInShuttle, true);
            // Boarding is walking in: a passenger needs no labor ability
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsWork();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            if (!agent.BeingData.HasStateFlag(BeingState.Expedition)) {
                agent.S.Sig.FailedToEnterShuttle.Send(agent.BeingData, goal?.FailReason);
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            ShuttleComp shuttle = null;
            if (goal.TryGetVar("ShuttleId", out int id)) {
                shuttle = S.Components.Find<ShuttleComp>(id);
            }
            if (shuttle == null || shuttle.IsLost ||
                    shuttle.IsOut || !shuttle.Tile.IsActive) {
                return Failed(T.AdRejTargetUnavailable);
            }
            if (agent.PosIdx != shuttle.InPort) {
                if (!TryGoToOnce(agent, shuttle.InPort, false)) {
                    D.Err("Failed enter shuttle: {0} | {1}",
                        agent.BB.MoveProgress, agent);
                    return FailedNavToTarget(agent, shuttle.InPort);
                }
                return InProgress;
            }
            // make sure that we are actually a member of the crew before
            // removing the being
            var exp = S.Sys.Expedition.FindForShuttle(shuttle.EntityId);
            if (exp == null || !exp.Crew.Contains(agent.BeingData)) {
                D.Err("Trying to enter shuttle while not a crew member for expedition: ({0}) -> {1}",
                    exp, agent);
                return Failed(T.AdRejTargetUnavailable);
            }

            // should be there
            agent.BeingData.StateFlags |= BeingState.Expedition;
            agent.BeingData.StateFlags |= BeingState.Hidden;
            agent.BeingData.LastPosition = agent.Motion.Position;
            S.Clock.OnNextTick(() => {
                S.Beings.Destroy(agent.Being);
                if (agent.BeingData.Being != null) {
                    D.Err("Being not null after entering shuttle! {0}", agent);
                }
            });
            goal.WithWorld(AIVarsH.IsInShuttle, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
