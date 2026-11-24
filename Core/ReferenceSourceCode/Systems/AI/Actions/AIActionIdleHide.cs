using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class AIActionIdleHide : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionIdleHide());
        }
        public const string ActId = "IdleHide";
        private AIActionIdleHide() : base(ActId, () => T.VerbHiding) {
            Preconditions = AIState.With(AIVarsH.IsIdle, false);
            Outcomes = AIState.With(AIVarsH.IsIdle, true);
            MarkAsQuiet();
            MarkAsRest();
            WithRequiredAbilities(AbilityIdH.IdleHide);
        }

        private bool IsGoodForHiding(AIAgentComp agent, Tile t) {
            if (!agent.IsFeelingSafeAt(t.PosIdx)) {
                return false;
            }
            return t.IsConstructed && t.Graphics.HasHeight;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Type == AIGoalType.Idle) {
                goal.WithInterrupt(1, 0);
            }
            var worker = agent.Being;
            agent.BB.Bool1NotSaved = false;
            var hideTarget = worker.S.Query.FindTileNearby.Ask(worker.PosIdx,
                worker.Motion.IsGrounded, 2, WorldLayer.Objects,
                    (t) => IsGoodForHiding(agent, t));
            if (hideTarget != null) {
                agent.BB.TargetEntity = hideTarget;
                int hidePos;
                if (hideTarget.Transform.IsMulti) {
                    hidePos = Pos.NeighborAt(hideTarget.PosIdx,
                        Rng.URange(0, hideTarget.Transform.RotatedWidth),
                        hideTarget.Transform.RotatedHeight);
                } else {
                    hidePos = Pos.N(hideTarget.PosIdx);
                }
                // let's go one tile above the target root and stay there
                if (!agent.Motion.TryGoTo(hidePos, PathReqFlags.AdjOnTop)) {
                    agent.BB.TargetEntity = null;
                }
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.TargetEntity != null) {
                if (agent.Motion.IsBusy) { return InProgress; }
                var pos = agent.PosIdx;
                if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                    if (beings.Count > 1) {
                        // shit, we have been found
                        var newPos = S.Query.RandomSafePosFrom.Ask(agent, 2, 0.1f);
                        if (newPos != Pos.Invalid) {
                            agent.Motion.TryGoTo(newPos, PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby);
                        }
                        return Failed(T.AdRejInterrupted);
                    }
                }
                if (agent.TicksInCurrentAction < Consts.TicksPer10Minutes &&
                        EntityUtils.IsNearObj(pos, agent.BB.TargetEntity as Tile)) {
                    if (!agent.BB.Bool1NotSaved) {
                        agent.BB.Bool1NotSaved = true;
                        // move close to the edge of the device
                        Vector2 off;
                        Facing.Type facing;
                        if (Rng.UChance(0.5f)) {
                            off = new Vector2(0.25f, -0.3f);
                            facing = Facing.Type.Right;
                        } else {
                            off = new Vector2(-0.25f, -0.3f);
                            facing = Facing.Type.Left;

                        }
                        var hidePos = EntityUtils.CenterOf(agent.PosIdx) + off;
                        agent.Motion.TeleportTo(hidePos);
                        agent.Being.Graphics.SetFacing(facing);
                    }
                    return InProgress;
                }
            }
            goal.WorldState.Set(AIVarsH.IsIdle, true);
            return Success;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetEntity = null;
            agent.BB.Bool1NotSaved = false;
            agent.BB.SetCooldown(IdH, ticks,
                Consts.TicksPer2Hours, Consts.TicksPer4Hours);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 1000f;
            }
            return Rng.UValue * 1.5f;
        }
    }
}
