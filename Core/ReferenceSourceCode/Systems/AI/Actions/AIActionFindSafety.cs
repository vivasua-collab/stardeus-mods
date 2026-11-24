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
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public sealed class AIActionFindSafety : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFindSafety());
        }
        public static StatModifier StatModRunToSafety = StatModifier.AddPercent(1.0f,
            T.ActMovetosafety);
        public const string ActId = "FindSafety";
        private AIActionFindSafety() : base(ActId, () => T.ActMovetosafety) {
            Preconditions = AIState.With(AIVarsH.IsInSafeEnvironment, false);
            Outcomes = AIState.With(AIVarsH.IsInSafeEnvironment, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            agent.BB.MoveTargetPos = 0;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var pos = agent.Motion.Position.Idx;

            if (agent.BeingData.Safety.IsActuallySafeAt(pos)) {
                goal.WorldState.Set(AIVarsH.IsInSafeEnvironment, true);
                return Success;
            }

            if (agent.BB.MoveProgress == null) {
                return FindSafety(S, agent, pos);
            }
            agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
            return Failed(T.FailedToFindPathToSafety_V1.F(Texts.LinkToPos(pos)));
        }

        public AIActionResult FindSafety(GameState S, AIAgentComp agent, int pos) {
            int wantedPos = FindSafePos(S, agent, pos);
            if (wantedPos != Pos.Invalid) {
                agent.BB.MoveTargetPos = wantedPos;
                if (agent.Motion.TryGoTo(wantedPos, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    agent.Being.Data.StatsBeing.MoveSpeed.AddModifier(
                        StatModRunToSafety, true);
                } else {
                    #if !PRODUCTION_BUILD
                    if (!The.TestsAreRunning) {
                        D.Warn("Could not run to safety: {0} ({1})-> {2}",
                            Pos.StringFromTo(pos, wantedPos),
                            agent.BB.MoveProgress.FailReason, agent);
                            agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                    }
                    #else
                    D.Warn("Could not run to safety: {0} ({1})-> {2}",
                        Pos.StringFromTo(pos, wantedPos),
                        agent.BB.MoveProgress.FailReason, agent);
                        agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                    #endif
                    return Failed(T.FailedToFindPathToSafety_V1.F(Texts.LinkToPos(pos)));
                }
            } else {
                D.Warn("Could not find where to roam to: {0}", agent);
                return Failed(T.FailedToFindPathToSafety_V1.F(Texts.LinkToPos(pos)));
            }

            return InProgress;
        }

        public static int FindSafePos(GameState S, AIAgentComp agent, int agentPos) {
            return FindSafePosFor(S, agent.BeingData, agent, agentPos);
        }

        // Safe for whom and walked by whom can differ: a rescuer carrying a downed colonist
        // needs a tile that is safe for the patient, reachable the way the carrier moves.
        // allowSaferFallback accepts a merely less dangerous tile when no safe one is near:
        // fine for someone fleeing on their own feet, but a rescue that ends on such a tile
        // leaves the patient to die in a different spot of the same vacuum.
        public static int FindSafePosFor(GameState S, BeingData forWhom, AIAgentComp agent,
                int agentPos, bool allowSaferFallback = true) {
            // find safety position to go to, try go there
            var sys = agent.Being.S.Sys;
            var safety = forWhom.Safety;
            var isG = agent.Motion.IsGrounded;
            var path = sys.Path;
            // int wantedPos = S.Query.RandomSafePosFrom.Ask(agent, 10, 0.5f);
            int foundPos = S.Query.FindNearby.Ask(agentPos, isG, 10, (p) => {
                if (p < 1 || agent.Motion.HadNavFailureAt(p)) {
                    return false;
                }
                return path.IsNavigable(p, isG) && safety.IsActuallySafeAt(p);
            });
            if (foundPos == Pos.Invalid && allowSaferFallback) {
                foundPos = S.Query.FindNearby.Ask(agentPos, isG, 10, (p) => {
                    if (p < 1 || agent.Motion.HadNavFailureAt(p)) {
                        return false;
                    }
                    return path.IsPassable(p) && safety.IsFeelingSaferAt(agentPos, p);
                });
            }
            return foundPos;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.Being.Data.StatsBeing.MoveSpeed.RemoveModifier(StatModRunToSafety, false);
            agent.BB.MoveProgress = null;
            agent.BB.MoveTargetPos = 0;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Go for it!!
            return 0.00001f;
        }
    }
}
