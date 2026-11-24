using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Path;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionMove : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionMove());
        }
        // stored in agent.BB.Byte1
        public enum Phase : byte {
            CheckReachability,
            ReachSource,
            PickUp,
            ReachTarget,
            PutDown,
            // for reaching an object that is stuck inside a multi-tile
            ReachStuckSource,
            PickUpStuck,
        }
        public const string ActId = "Move";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionMove() : base(ActId, () => T.ActMoveobj) {
            Preconditions = AIState.With(AIVarsH.IsMoved, false);
            Outcomes = AIState.With(AIVarsH.IsMoved, true);
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Carry);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public Phase GetPhase(AIAgentComp agent) {
            return (Phase) agent.BB.Byte1;
        }
        public void SetPhase(AIAgentComp agent, Phase phase) {
            agent.BB.Byte1 = (byte) phase;
        }

        public bool TryGetMovable(AIGoal goal, out MovableComp mov) {
            if (goal.Target is MovableComp movable) {
                mov = movable;
                return mov?.Entity?.IsActive == true;
            }
            mov = null;
            return false;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // phase
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            var c = agent.Being.Graphics.Carrying;
            if (c != null) {
                if (c is Obj o) {
                    o.Pile?.ReaddToInventoryIfNeeded();
                }
            }

            agent.Being.Graphics.DropIfCarrying();

            // it could be false if one of the entities are removed
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var phase = GetPhase(agent);
            if (agent.Motion.IsBusy) {
                if (phase == Phase.ReachSource) {
                    // make pickup happen sooner when we are chasing an object that moves
                    if (TryGetMovable(goal, out var mov)) {
                        if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, mov.Entity)) {
                            if (!PickUpObj(agent, goal, mov, out var failRes)) {
                                return failRes;
                            }
                            // this will reset the last motion so we can TryGoToOnce again
                            agent.BB.MoveProgress = null;
                            SetPhase(agent, Phase.ReachTarget);
                            agent.Motion.Abort(T.AdRejCancelled);
                        }
                    }
                }
                return InProgress;
            }
            switch (phase) {
                case Phase.CheckReachability: return CheckReachability(goal, agent);
                case Phase.ReachSource: return ReachSource(goal, agent);
                case Phase.PickUp: return PickUp(goal, agent);
                case Phase.ReachTarget: return ReachTarget(goal, agent);
                case Phase.PutDown: return PutDown(goal, agent);
                case Phase.ReachStuckSource: return ReachStuckSource(goal, agent);
                case Phase.PickUpStuck: return PickUpStuck(goal, agent);
            }

            return Error("Unhandled phase in goal: {0}: {1}", phase, goal);
        }
        private AIActionResult PutDown(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                var endPos = agent.Motion.Position.Idx;
                if (Pos.AreAdjacent(endPos, mov.MoveTargetPos)) {
                    var carriedEntity = agent.Being.Graphics.Carrying;
                    if (carriedEntity == mov.Entity) {
                        agent.Being.Graphics.Drop(carriedEntity);
                        if (carriedEntity is Being b) {
                            b.Motion.TeleportTo(mov.MoveTarget);
                            if (b.Data.Body.IsInjured) {
                                // see if we are dropping the being onto an in port
                                // of a healing device or a bed
                                AutoDocComp autoDoc = null;
                                Pos.ForEachAround(endPos, 1, true, false, (p) => {
                                    var topmost = EntityUtils.GetTopmostParentTile(p);
                                    if (topmost == null) { return true; }
                                    if (topmost.Transform.HasInPort
                                            && topmost.Transform.InPort == endPos) {
                                        if (topmost.TryGetComponent<AutoDocComp>(out var doc)) {
                                            autoDoc = doc;
                                            if (doc.HasFreeSlot) {
                                                // found autodoc with free slot
                                                return false;
                                            }
                                        }
                                    }
                                    return true;
                                });
                                if (autoDoc != null) {
                                    var slot = autoDoc.Slots[0];
                                    if (slot.ContainedBeing != null) {
                                        slot.Unreserve(slot.ContainedBeing);
                                        slot.Remove(slot.ContainedBeing);
                                    }
                                    if (slot.Put(b)) {
                                        AINeedHealth healthNeed = (AINeedHealth)
                                            Registries.AINeeds.FastGet(NeedIdH.Health);
                                        if (!healthNeed.ForceStartHealing(goal, b, slot, G.Ticks)) {
                                            D.Err("Failed starting healing for being: {0}", b);
                                            slot.Remove(b);
                                        }
                                    }
                                }
                            }
                        } else if (carriedEntity is Obj o && o.IsActive) {
                            o.Transform.TeleportTo(mov.MoveTarget);
                            o.Graphics.TeleportTo(mov.MoveTarget);
                        }
                        goal.WithWorld(AIVarsH.IsMoved, true);
                        agent.Motion.TryStepAside();
                        return Success;
                    } else {
                        D.Err("Trying to put down movable that we are not carrying! {0} | {1}. Movable: {2}. Carried: {3}",
                            goal, agent, mov.Entity, carriedEntity);
                    }
                } else {
                    return FailedNavToTarget(agent, mov.MoveTargetPos);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult PickUp(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, mov.Entity)) {
                    if (!PickUpObj(agent, goal, mov, out var failRes)) {
                        return failRes;
                    }
                    // this will reset the last motion so we can TryGoToOnce again
                    agent.BB.MoveProgress = null;
                    SetPhase(agent, Phase.ReachTarget);
                    return InProgress;
                } else {
                    return FailedNavToTarget(agent, mov.Entity.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult PickUpStuck(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                if (!PickUpObj(agent, goal, mov, out var failRes)) {
                    return failRes;
                }
                SetPhase(agent, Phase.ReachTarget);
                return InProgress;
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private bool PickUpObj(AIAgentComp agent, AIGoal goal, MovableComp mov,
                out AIActionResult failRes) {
            var obj = mov.Entity;
            // If it's a pile, we must reserve it first and remove from inventory
            if (obj is Obj o && o.Pile != null) {
                if (o.Pile.TreatAsObj) {
                    o.Pile.RemoveFromInventory();
                } else {
                    var maxCarry = agent.SpareCarryAmount;
                    if (maxCarry == 0) {
                        D.Err("Agent has 0 carry capacity! {0}", agent.Being);
                        failRes = Failed(T.GoalWorkerIncapable);
                        return false;
                    }
                    var proxy = mov.RemoveProxy();
                    var toReserve = Maths.Min(maxCarry, o.Pile.StackSize);
                    var took = o.Pile.ReserveAndTake(o.Pile.Type, agent.Being,
                        toReserve, goal);

                    if (took < 1) {
                        mov.SetProxy(proxy);
                        failRes = Failed(T.AdRejCouldNotReserve);
                        return false;
                    }

                    var carriedMat = EntityUtils.SpawnRawMaterial(o.Pile.Type,
                        took, agent.Being.PosIdx, 0, true, false);
                    carriedMat.RemoveFromInventory();
                    carriedMat.IsForbidden = o.Pile.IsForbidden;
                    var oldTarget = goal.Target as MovableComp;
                    carriedMat.Obj.Movable.MoveTarget = oldTarget.MoveTarget;
                    goal.ReattachTo(carriedMat.Obj.Movable);
                    carriedMat.Obj.Movable.SetProxy(proxy);
                    obj = carriedMat.Obj;
                    if (o.Pile.StackSize > 0) {
                        // we have to split the pile and create a copy of move goal for
                        // the remaining pile
                        if (!oldTarget.OrderMove(oldTarget.MoveTarget)) {
                            D.Err("Failed ordering move of the rest of the pile: {0} => {1}",
                                goal.Target.Entity, goal);
                        }
                    }
                }
            }

            // 2. if not, pick the object up
            if (obj is Obj o2) {
                o2.Transform.TeleportTo(agent.Being.Position);
                o2.Graphics.TeleportTo(agent.Being.Position);
                o2.Pile?.ReaddToInventoryIfNeeded();
                if (!agent.Being.Graphics.Carry(o2, true, false)) {
                    failRes = Failed(T.AdRejTargetUnavailable);
                    return false;
                }
            } else if (obj is Being b) {
                if (b.Data.HasStateFlag(BeingState.InDevice)
                        && RepairStationComp.TryGetAt(b.PosIdx, out var station)) {
                    station.ReleaseForPickup(b);
                }
                b.Motion.TeleportTo(agent.Being.Position);
                if (!agent.Being.Graphics.Carry(b)) {
                    failRes = Failed(T.AdRejTargetUnavailable);
                    return false;
                }
            } else {
                D.Err("Unsupported entity for carrying: {0}", obj);
                failRes = Failed(T.Error);
                return false;
            }
            failRes = default;
            return true;
        }

        private AIActionResult ReachSource(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, mov.Entity)) {
                    SetPhase(agent, Phase.PickUp);
                    return InProgress;
                } else {
                    if (TryGoToOnce(agent, mov.Entity, false)) {
                        // location may change, so we'll clear the target after we start moving
                        agent.BB.ClearMoveTarget();
                        return InProgress;
                    }
                    return FailedNavToTarget(agent);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }
        private AIActionResult ReachStuckSource(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                var topmost = EntityUtils.GetTopmostParentTile(mov.Entity.PosIdx);
                if (topmost == null) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, topmost)) {
                    SetPhase(agent, Phase.PickUpStuck);
                    return InProgress;
                } else {
                    if (TryGoToOnce(agent, topmost, false)) {
                        // location may change, so we'll clear the target after we start moving
                        agent.BB.ClearMoveTarget();
                        return InProgress;
                    }
                    return FailedNavToTarget(agent);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }
        private AIActionResult ReachTarget(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                if (Pos.AreAdjacent(agent.Motion.Position.Idx, mov.MoveTargetPos)) {
                    SetPhase(agent, Phase.PutDown);
                    return InProgress;
                } else {
                    if (TryGoToOnce(agent, mov.MoveTargetPos, false)) {
                        if (mov.Entity.Carrier == null) {
                            D.Err("Carrier not set in move action, could be OnLoad! {0} | {1}",
                                goal, mov.Entity);
                            if (!agent.Being.Graphics.Carry(mov.Entity)) {
                                return Failed(T.AdRejTargetUnavailable);
                            }
                        }
                        return InProgress;
                    }
                    return FailedNavToTarget(agent);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult CheckReachability(AIGoal goal, AIAgentComp agent) {
            if (TryGetMovable(goal, out var mov)) {
                var flags = PathReqFlags.AdjNearby | PathReqFlags.Instant;
                var targetPos = mov.MoveTargetPos;
                if (targetPos == 0) {
                    D.Err("Cancelling invalid move task, target pos is 0! {0} | {1}",
                        goal, goal.Target);
                    return Cancelled(T.AdRejTargetUnavailable);
                }
                if (agent.Motion.EstimatePathTo(mov.Entity.PosIdx, null, flags)) {
                    if (agent.Motion.EstimatePathTo(mov.MoveTargetPos, null, flags)) {
                        SetPhase(agent, Phase.ReachSource);
                        return InProgress;
                    } else {
                        // we will also mark path to target as failed
                        // to prevent retry
                        agent.Motion.StoreNavFailureAt(mov.Entity.PosIdx);
                        return FailedNavToTarget(agent, mov.MoveTargetPos);
                    }
                } else {
                    // is the object stuck?
                    var pos = mov.Entity.PosIdx;
                    if (!agent.S.Sys.Path.IsPassable(pos)) {
                        var topmost = EntityUtils.GetTopmostParentTile(pos);
                        if (topmost != null) {
                            D.Warn("movable object stuck inside: {0} -> {1}", mov, topmost);
                            if (agent.Motion.EstimatePathTo(topmost, null, flags)) {
                                SetPhase(agent, Phase.ReachStuckSource);
                                return InProgress;
                            }
                        }
                    }

                    return FailedNavToTarget(agent, mov.Entity.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // never try to move yourself
            if (agent.Being.Id == goal.Target.EntityId) {
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 2f;
        }
    }
}
