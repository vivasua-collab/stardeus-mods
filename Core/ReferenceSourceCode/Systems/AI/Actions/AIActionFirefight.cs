using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionFirefight : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFirefight());
        }
        public const string ActId = "Firefight";
        public readonly static int ActIdH = Hashes.S(ActId);
        private int ticksToExtinguish => Mathf.RoundToInt(Rng.UCurve(
            Tunables.FireExtinguishMinutesCurve) * Consts.TicksPerMinute);
        private AIActionFirefight() : base(ActId, () => T.ActFirefight) {
            Preconditions = AIState.With(AIVarsH.IsFireExtinguished, false);
            Outcomes = AIState.With(AIVarsH.IsFireExtinguished, true);
            WithRequiredJobType(JobTypeIdH.Firefighting);
            WithRequiredAbilities(AbilityIdH.DumbLabor);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetEntity = null;
            agent.BB.Float1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetEntity = null;
            agent.BB.Float1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
            agent.S.Sys.Firex.Firefight.EndFirefighting(agent.Being);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var firex = S.Sys.Firex;
            // 0. check if anything is on fire
            if (firex.OnFireCount < 1) {
                goal.WorldState.Set(AIVarsH.IsFireExtinguished, true);
                return Success;
            }

            if (!agent.Jobs.CanDo(JobTypeIdH.Firefighting)) {
                goal.WorldState.Set(AIVarsH.IsFireExtinguished, true);
                return Success;
            }

            if (agent.Motion.IsBusy) {
                if (agent.BB.TargetEntity != null) {
                    if (!goal.IsForceAssigned && firex.Firefight.HasEnoughFirefightersFor(agent.Being,
                            agent.BB.TargetEntity.PosIdx)) {
                        // haven't reached the fire, but there's enough firefighters
                        // there right now already, we can pick another flame
                        // to fight
                        agent.BB.TargetEntity = null;
                        agent.BB.Bool1NotSaved = false;
                        agent.Motion.Abort(T.ActFirefight);
                    }
                }
                return InProgress;
            }

            // 1. figure out closest fire
            float sqrDistToClosestFire = 0f;
            if (agent.BB.TargetEntity == null) {
                if (goal.TryGetVar(AIGoalVars.TargetPos, out int targetPos)) {
                    goal.ClearVars();
                    agent.BB.TargetEntity = firex.GetFireAt(targetPos);
                } else {
                    agent.BB.TargetEntity = firex.GetClosestReachableFireFor(agent.Being,
                        out sqrDistToClosestFire);
                }
                if (agent.BB.TargetEntity != null) {
                    agent.BB.Float1 = 1f / ticksToExtinguish;
                }
            }

            var target = agent.BB.TargetEntity;
            if (target == null || !target.IsActive) {
                CooldownFirefighting(agent, sqrDistToClosestFire, ticks);
                return Failed(T.AdRejTargetUnavailable);
            }

            bool isNear;
            int agentPos = agent.PosIdx;
            if (target is Tile t) {
                isNear = EntityUtils.IsNearObj(agentPos, t);
            } else {
                isNear = Pos.SqrDistance(agentPos, target.PosIdx) < 2;
            }

            // 2. go to closest fire
            if (isNear) {
                if (agent.Jobs.TryJob(JobTypeIdH.Firefighting, out var job)) {
                    var pos = agent.BB.TargetEntity.Position;
                    if (firex.AddFirefightingEffortAt(target.PosIdx,
                            agent.BB.Float1 * job.UseSkill(SkillXp.Firefighting) * job.WorkSpeedMul())) {
                        if (!agent.BB.Bool1NotSaved) {
                            agent.BB.Bool1NotSaved = true;
                            firex.Firefight.MarkFirefighting(agent.Being,
                                agent.BB.TargetEntity.PosIdx);
                        }
                        FacePosition(agent, pos);
                        var punchDir = pos - agent.Motion.Position.Exact;
                        agent.Being.Graphics.Punch(punchDir , 0.3f, 0.1f, 0.1f);
                    } else {
                        // fire is out
                        agent.BB.MoveProgress = null;
                        agent.BB.MoveTargetPos = Pos.Invalid;
                        agent.BB.TargetEntity = null;
                        agent.BB.Bool1NotSaved = false;
                        firex.Firefight.EndFirefighting(agent.Being);
                        if (agent.DirectControl.IsUnderDirectControl) {
                            // we do not take the next fire on our own if we are
                            // under direct control
                            goal.WorldState.Set(AIVarsH.IsFireExtinguished, true);
                            return Success;
                        }
                    }
                    return InProgress;
                } else {
                    return Error("Agent who is firefighting has no job for this: {0}", agent);
                }
            } else {
                if (TryGoToOnce(agent, target, true, PathReqFlags.SurvivableOK)) {
                    return InProgress;
                }
                return FailedNavToTarget(agent);
            }
        }

        private void CooldownFirefighting(AIAgentComp agent, float sqrDistToClosestFire, long ticks) {
            int cooldown;
            bool isGrounded = agent.Motion.IsGrounded;
            if (sqrDistToClosestFire < 10 * 10) {
                cooldown = Consts.TicksPer5Minutes;
            } else if (sqrDistToClosestFire < 20 * 20) {
                cooldown = Consts.TicksPer15Minutes;
            } else if (sqrDistToClosestFire < 30 * 30) {
                cooldown = Consts.TicksPer30Minutes;
            } else {
                cooldown = Consts.TicksPerHour;
            }
            if (isGrounded) {
                cooldown *= 2;
            }
            agent.BB.SetCooldown(JobTypeIdH.Firefighting, ticks, cooldown,
                Mathf.RoundToInt(cooldown * 1.2f));
        }
    }
}
