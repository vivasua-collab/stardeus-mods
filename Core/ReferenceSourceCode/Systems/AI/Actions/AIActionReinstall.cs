using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionReinstall : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionReinstall());
        }
        // stored in agent.BB.Byte1
        public enum Phase : byte {
            CheckReachability,
            ReachSource,
            PickUpTile,
            ReachTarget,
            PutDownTile
        }
        public const string ActId = "Reinstall";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionReinstall() : base(ActId, () => T.ActMoveobj) {
            Preconditions = AIState.With(AIVarsH.IsReinstalled, false);
            Outcomes = AIState.With(AIVarsH.IsReinstalled, true);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.Carry);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public Phase GetPhase(AIAgentComp agent) {
            return (Phase) agent.BB.Byte1;
        }
        public void SetPhase(AIAgentComp agent, Phase phase) {
            agent.BB.Byte1 = (byte) phase;
        }

        public bool TryGetSourceTarget(AIGoal goal,
                out ReinstallableComp src,
                out ReinstallableComp target) {
            if (goal.Target is ReinstallableComp re) {
                src = re.Source;
                target = re;
                return src?.Entity?.IsActive == true
                    && target.Entity?.IsActive == true;
            }
            src = null;
            target = null;
            return false;
        }
        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Byte1 = 0;
            agent.BB.MoveTargetPos = Pos.Invalid;
            agent.BB.MoveProgress = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveTargetPos = Pos.Invalid;
            agent.BB.MoveProgress = null;
            var c = agent.Being.Graphics.Carrying;
            if (c != null) {
                if (c is Obj o) {
                    o.Pile?.ReaddToInventoryIfNeeded();
                }
            }
            agent.Being.Graphics.DropIfCarrying();

            agent.BB.ClearFauxObj();
            agent.BB.ClearBlacklist();
            // it could be false if one of the entities are removed
            TryGetSourceTarget(goal, out var source, out var target);
            if (source?.Entity.IsActive == true) {
                source.SetSourceUnbuiltOverride(false);
                source.IsMidRelocation = false;
                // we likely failed, let's restore the looks
                source.Tile.Graphics.HasShader = false;
                source.Tile.Graphics.Shader = null;
                EntityUtils.TriggerChange(source.Tile, updatePassability: false);
            }
            if (target != null) {
                target.IsMidRelocation = false;
            }
            agent.BB.Byte1 = 0;
            agent.BB.MoveTargetPos = Pos.Invalid;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var phase = GetPhase(agent);
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            switch (phase) {
                case Phase.CheckReachability: return CheckReachability(S, goal, agent);
                case Phase.ReachSource: return ReachSource(goal, agent);
                case Phase.PickUpTile: return PickUpTile(goal, agent);
                case Phase.ReachTarget: return ReachTarget(goal, agent);
                case Phase.PutDownTile: return PutDownTile(goal, agent);
            }

            return Error("Unhandled phase in goal: {0}: {1}", phase, goal);
        }
        private AIActionResult PutDownTile(AIGoal goal, AIAgentComp agent) {
            if (TryGetSourceTarget(goal, out var src, out var target)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, target.Tile)) {
                    AIActionConstruct.MoveObstructions(agent, target.Tile);
                    target.IsMidRelocation = true;
                    src.IsMidRelocation = true;
                    // finalize move has to go after move obstructions, or
                    // plants will be fucked
                    target.FinalizeMove();
                    agent.BB.ClearFauxObj();
                    goal.WithWorld(AIVarsH.IsReinstalled, true);

                    return Success;
                } else {
                    return FailedNavToTarget(agent, src.Tile.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult PickUpTile(AIGoal goal, AIAgentComp agent) {
            if (TryGetSourceTarget(goal, out var src, out var target)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, src.Tile)) {
                    src.IsMidRelocation = true;
                    target.IsMidRelocation = true;
                    src.SetSourceUnbuiltOverride(true);
                    EnsureFauxObj(agent, goal);
                    // this will reset the last motion so we can TryGoToOnce again
                    agent.BB.MoveProgress = null;
                    SetPhase(agent, Phase.ReachTarget);
                    return InProgress;
                } else {
                    return FailedNavToTarget(agent, src.Tile.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult ReachSource(AIGoal goal, AIAgentComp agent) {
            if (TryGetSourceTarget(goal, out var src, out _)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, src.Tile)) {
                    SetPhase(agent, Phase.PickUpTile);
                    return InProgress;
                } else {
                    if (TryGoToOnce(agent, src.Tile)) {
                        return InProgress;
                    }
                    return FailedNavToTarget(agent, src.Tile.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }
        private AIActionResult ReachTarget(AIGoal goal, AIAgentComp agent) {
            if (TryGetSourceTarget(goal, out _, out var target)) {
                if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, target.Tile)) {
                    SetPhase(agent, Phase.PutDownTile);
                    return InProgress;
                } else {
                    if (TryGoToOnce(agent, target.Tile)) {
                        return InProgress;
                    }
                    return FailedNavToTarget(agent, target.Tile.PosIdx);
                }
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private AIActionResult CheckReachability(GameState S, AIGoal goal, AIAgentComp agent) {
            if (TryGetSourceTarget(goal, out var src, out var target)) {
                var flags = PathReqFlags.AdjNearby | PathReqFlags.Instant;

                // Check source first: the worker must be able to reach it.
                if (!agent.Motion.EstimatePathTo(src.Tile, null, flags)) {
                    return FailedNavToTarget(agent, src.Tile.PosIdx);
                }

                // Check target. If it appears unreachable, it may be because the source
                // object is blocking the room's only entrance. If source and target share
                // the same enclosed room, proceed optimistically: once the worker picks up
                // the source the blockage is cleared and ReachTarget will nav there normally.
                bool targetReachable = agent.Motion.EstimatePathTo(target.Tile, null, flags);
                if (!targetReachable) {
                    if (!S.Sys.Areas.IsSameAreaIndoors(
                            src.Tile.PosIdx, target.Tile.PosIdx)) {
                        // Genuinely different room — we will not be able to deliver there.
                        agent.Motion.StoreNavFailureAt(target.Tile.PosIdx);
                        return FailedNavToTarget(agent, target.Tile.PosIdx);
                    }
                    // Same room: the source is likely the blocker — proceed.
                }

                if (!src.Tile.Constructable.IsBuiltInSpace) {
                    // check if target floor is built
                    int cnt = target.Tile.Transform.GetSubtilePos(Caches.IntArray128);
                    bool isAnyFloorUnbuilt = false;
                    bool isAnyFloorMissing = false;
                    for (int i = 0; i < cnt; i++) {
                        var fl = EntityUtils.FloorAt(Caches.IntArray128[i]);
                        if (fl == null) {
                            isAnyFloorMissing = true;
                            break;
                        } else if (!fl.IsConstructed) {
                            isAnyFloorUnbuilt = true;
                        }
                    }
                    if (isAnyFloorMissing) {
                        AIGoal.Cancel(goal, G.Ticks, T.AdRejNoFloor, removeImmediately: false);
                        return Failed(T.AdRejNoFloor);
                    }
                    if (isAnyFloorUnbuilt) {
                        return Failed(T.AdRejFloorNotConstructed);
                    }
                }

                SetPhase(agent, Phase.ReachSource);
                return InProgress;
            }
            return Failed(T.AdRejTargetUnavailable);
        }

        private void EnsureFauxObj(AIAgentComp agent, AIGoal goal) {
            if (agent.BB.FauxObj != null) { return; }
            if (!TryGetSourceTarget(goal, out var source, out _)) {
                return;
            }
            var g = source.Tile.Graphics;
            g.Shader = RenderingService.Shaders.Get("UnconstructedTile");
            g.HasShader = true;
            EntityUtils.TriggerChange(source.Tile, updatePassability: false);
            // var worker = agent.Being;
            var sd = g.IconSD.Copy();
            sd.Scale = g.OriginalGraphicSize;
            agent.BB.CreateFauxObj(sd);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            int targetPos;
            if (goal.Target is ReinstallableComp re) {
                if (re.Source == null) {
                    D.Err("Relocation target has no source! {0}", goal);
                    targetPos = goal.Target.Entity.PosIdx;
                } else {
                    targetPos = re.Source.Tile.PosIdx;
                }
            } else {
                D.Err("Reinstallable goal target is not ReinstallableComp! {0}", goal);
                targetPos = goal.Target.Entity.PosIdx;
            }
            return agent.Motion.GoToCost(targetPos) * 2f;
        }
    }
}
