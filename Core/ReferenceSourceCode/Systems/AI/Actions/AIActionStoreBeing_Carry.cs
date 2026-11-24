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
using UnityEngine.Pool;

namespace Game.Systems.AI {
    public sealed class AIActionStoreBeing_Carry : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStoreBeing_Carry());
        }
        public const string ActId = "Storage_Carry";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionStoreBeing_Carry() : base(ActId, () => T.CarryingBeing) {
            Preconditions = AIState.With(AIVarsH.IsBeingStored, false)
                .And(AIVarsH.IsEquipmentStripped, true);
            Outcomes = AIState.With(AIVarsH.IsBeingStored, true);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.Carry);
            MarkAsWork();
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
            agent.Being.Graphics.DropIfCarrying();
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            if (goal.Target is not StorableBeingComp storable) {
                return Error("Goal target not storable: {0}", goal);
            }

            if (!agent.Being.Graphics.IsCarrying()) {
                if (EntityUtils.IsNearObj(agent.PosIdx, storable.Being)) {
                    if (!agent.Being.Graphics.Carry(storable.Being)) {
                        return Failed(T.AdRejTargetUnavailable);
                    }
                } else {
                    if (!agent.Motion.TryGoTo(storable.Being.PosIdx,
                            PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination)) {
                        return FailedNavToTarget(agent);
                    }
                    // chasing someone for 3 hours?
                    if (agent.TicksInCurrentAction > Consts.TicksPer3Hours) {
                        return FailedSoft(T.AdRejTargetUnavailable);
                    }
                    return InProgress;
                }
            }

            if (agent.BB.Object1 is not BeingStorageComp storage) {
                return FindStorage(S, agent, storable, goal, ticks);
            }

            // check if we are at the storage
            if (agent.PosIdx != agent.BB.TargetTile.Transform.InPort) {
                // failed to arrive
                return TryAnotherStorage(agent);
            }

            agent.Being.Graphics.DropIfCarrying();
            if (storage.Store(agent, storable.Being.Data, goal)) {
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

        private AIActionResult FindStorage(GameState S, AIAgentComp agent,
                StorableBeingComp storable, AIGoal goal, long ticks) {
            var isG = agent.Motion.IsGrounded;
            int agentIslandId = 0;
            int agentPos = agent.PosIdx;
            if (isG) {
                agentIslandId = S.Sys.Areas.IslandAt(agentPos);
            }

            // 1. find where to go
            var storages = ListPool<BeingStorageComp>.Get();
            int found = S.Query.BeingStoragesFor.Ask(storable.Being.Data, storages);

            if (found == 0) {
                ListPool<BeingStorageComp>.Release(storages);
                return Failed(T.StorageUnavailable);
            }
            BeingStorageComp closest = null;
            int bestPriority = 0;
            float bestDist = float.MaxValue;
            // True when at least one storage was skipped because it was unsafe or
            // on an unreachable island for this specific worker (but not because it
            // was full/rejected — that's a global condition, not worker-specific).
            bool existsButUnreachable = false;
            for (int i = storages.Count - 1; i >= 0; i--) {
                BeingStorageComp s = storages[i];
                if (s.HaulTarget?.HaulPriority == 0) {
                    continue;
                }
                if (agent.BB.IsBlacklisted(s.EntityId)) {
                    continue;
                }
                if (s.IsFull || (s.HaulTarget != null
                        && !s.HaulTarget.Accepts(storable.Being.Data))) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    continue;
                }
                var sPos = s.InPort;
                if (!agent.IsFeelingSafeAt(sPos)) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    existsButUnreachable = true;
                    continue;
                }
                if (isG) {
                    if (!S.Sys.Areas.CanReachGroundedIslandToPos(
                            agentIslandId, sPos)) {
                        agent.BB.AddToBlacklist(s.EntityId);
                        existsButUnreachable = true;
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
                if (existsButUnreachable) {
                    // A storage exists but this worker can't reach/enter it safely.
                    // Put the goal back so another worker can try, and cool this
                    // worker down so it doesn't immediately re-win the bid and loop.
                    agent.BB.SetGoalCooldown(goal.Id, ticks,
                        Consts.TicksPer2Hours, Consts.TicksPer4Hours);
                    return FailedSoft(T.NavFailUnreachableGrounded);
                }
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
                // cannot do this for ourselves, there is a _GoTo action for it
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 3f;
        }
    }
}
