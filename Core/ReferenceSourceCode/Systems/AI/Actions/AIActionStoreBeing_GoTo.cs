using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using KL.Grid;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using static Game.Components.BodyModSpotComp;
using UnityEngine.Pool;

namespace Game.Systems.AI {
    public sealed class AIActionStoreBeing_GoTo : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStoreBeing_GoTo());
        }
        public const string ActId = "Storage_GoTo";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionStoreBeing_GoTo() : base(ActId, () => T.VerbGoingTo_V1.F(T.NounBeingStorage)) {
            Preconditions = AIState.With(AIVarsH.IsBeingStored, false);
            Outcomes = AIState.With(AIVarsH.IsBeingStored, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // storage tile
            agent.BB.TargetTile = null;
            // beingstoragecomp
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // storage tile
            agent.BB.TargetTile = null;
            // beingstoragecomp
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.Object1 is not BeingStorageComp storage) {
                return FindStorage(S, agent);
            }

            // check if we are at the storage
            if (agent.PosIdx != agent.BB.TargetTile.Transform.InPort) {
                // failed to arrive
                return TryAnotherStorage(agent);
            }

            if (storage.Store(agent, agent.BeingData, goal)) {
                goal.WorldState.Set(AIVarsH.IsBeingStored, true);
                return Success;
            }

            return TryAnotherStorage(agent);
        }

        private AIActionResult TryAnotherStorage(AIAgentComp agent) {
            agent.BB.AddToBlacklist(agent.BB.TargetTile.Id);
            agent.BB.TargetTile = null;
            agent.BB.Object1 = null;
            // try another one next iteration
            return InProgress;
        }

        private AIActionResult FindStorage(GameState S, AIAgentComp agent) {
            var isG = agent.Motion.IsGrounded;
            int agentIslandId = 0;
            int agentPos = agent.PosIdx;
            if (isG) {
                agentIslandId = S.Sys.Areas.IslandAt(agentPos);
            }

            // 1. find where to go
            var storages = ListPool<BeingStorageComp>.Get();
            int found = S.Query.BeingStoragesFor.Ask(agent.BeingData, storages);

            if (found == 0) {
                ListPool<BeingStorageComp>.Release(storages);
                return Failed(T.StorageUnavailable);
            }
            BeingStorageComp closest = null;
            int bestPriority = 0;
            float bestDist = float.MaxValue;
            for (int i = storages.Count - 1; i >= 0; i--) {
                BeingStorageComp s = storages[i];
                if (s.HaulTarget?.HaulPriority == 0) {
                    continue;
                }
                if (agent.BB.IsBlacklisted(s.EntityId)) {
                    continue;
                }
                if (s.IsFull || (s.HaulTarget != null
                        && !s.HaulTarget.Accepts(agent.BeingData))) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    continue;
                }
                var sPos = s.InPort;
                if (!agent.IsFeelingSafeAt(sPos)) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    continue;
                }
                if (isG) {
                    if (!S.Sys.Areas.CanReachGroundedIslandToPos(
                            agentIslandId, sPos)) {
                        agent.BB.AddToBlacklist(s.EntityId);
                        continue;
                    }
                }
                int priority = s.HaulTarget?.HaulPriority ?? 5;
                float dist = Pos.SqrDistance(agentPos, sPos);
                if (EntityUtils.IsBetterStorage(priority, dist, bestPriority, bestDist)) {
                    closest = s;
                    bestPriority = priority;
                    bestDist = dist;
                }
            }
            storages.Clear();
            ListPool<BeingStorageComp>.Release(storages);
            if (closest == null) {
                return Failed(T.StorageUnavailable);
            }
            // try to go to closest
            agent.BB.MoveTargetPos = closest.InPort;
            if (!agent.Motion.TryGoTo(closest.InPort,
                    PathReqFlags.AdjOnTop | PathReqFlags.TempDestination, out agent.BB.MoveProgress)) {
                agent.Motion.StoreNavFailureAt(closest.InPort);
                agent.BB.AddToBlacklist(closest.EntityId);
                // try next iteration
                return InProgress;
            }
            // memorize the storage we are going towards
            agent.BB.TargetTile = closest.Tile;
            agent.BB.Object1 = closest;
            return InProgress;
        }


        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target.Entity == agent.Being) {
                if (agent.Motion.IsImmobilized) {
                    return 0f;
                }
                if (agent.BB.IsCoolingDown(IdH, ticks)) {
                    return 0f;
                }
                return 1f;
            }
            return 0f;
        }
    }
}
