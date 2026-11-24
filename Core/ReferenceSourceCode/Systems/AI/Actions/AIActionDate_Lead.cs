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
using Game.Systems.Stats;
using Game.Systems.Motion;

namespace Game.Systems.AI {
    public sealed class AIActionDate_Lead : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionDate_Lead());
        }
        public const string ActId = "DateLead";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionDate_Lead() : base(ActId, () => T.VerbDating) {
            Preconditions = AIState.With(AIVarsH.HadDateWith_Lead, false);
            Outcomes = AIState.With(AIVarsH.HadDateWith_Lead, true);
            WithRequiredJobType(JobTypeIdH.Social);
            RequireMentalHealth();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // admirable
            agent.BB.Object1 = null;
            // speed modifier
            agent.BB.Object2 = null;
            // number of activities done
            agent.BB.Int1 = 0;
            // date
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // admirable
            agent.BB.Object1 = null;
            // date
            agent.BB.TargetBeing = null;
            // number of activities done
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
            if (agent.BB.Object2 is StatModifier mod) {
                agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(mod, false);
            }
            agent.BB.Object2 = null;
            SetStandOffset(agent.Being, Vector2.zero);
        }

        // Half the distance between a couple standing on one tile
        private static readonly Vector2 SideBySide = new(0.3f, 0f);

        // Walking clears the offset by itself (BeingGraphicsComp)
        internal static void SetStandOffset(Being being, Vector2 offset) {
            if (being == null || !being.IsActive) { return; }
            var graphics = being.Graphics;
            if (graphics != null && graphics.PositionOffset != offset) {
                graphics.SetOffset(offset);
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounDate}");
            }

            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill();
            }

            var date = agent.BB.TargetBeing;

            // face each other when possible
            if (!date.Motion.IsBusy) {
                // Both walk to the same tile and stood in one spot there, drawn as one being
                // (the old push away from each other had no direction to push in). They stand
                // side by side instead.
                var follows = date.AIAgent.CurrentAction?.IdH == AIActionDate_Follow.ActIdH;
                var side = follows && date.PosIdx == agent.PosIdx ? SideBySide : Vector2.zero;
                SetStandOffset(agent.Being, -side);
                if (follows) {
                    SetStandOffset(date, side);
                }
                FacePosition(agent, date.Position);
                FacePosition(date.AIAgent, agent.Position);
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    date.AIAgent.CurrentAction?.IdH != AIActionDate_Follow.ActIdH) {
                // our follower is missing
                return Failed(T.DateCutShort_V1.F(date.Data.Mind.LinkToShortName));
            }

            if (agent.BB.Int1 % 2 == 0) {
                if (!date.Motion.IsBusy &&
                        agent.Motion.TicksInCurrentPosition > Consts.TicksPer15Minutes
                        && date.Motion.TicksInCurrentPosition > Consts.TicksPer15Minutes) {

                    if (agent.TicksInCurrentAction > Consts.TicksPer6Hours) {
                        goal.WorldState.Set(AIVarsH.HadDateWith_Lead, true);
                        return Success;
                    }

                    // roam
                    if (AIActionIdleRoam.TryRoam(agent, false, 2, instant: true)) {
                        var reqPos = agent.Motion.Request.PosTo;
                        if (reqPos > 0 && date.Motion.TryGoTo(reqPos,
                                PathReqFlags.AdjOnTop |
                                PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                            agent.Motion.Abort("Stay with date");
                        }
                    }
                    IncreaseDatePhase(agent);
                }
                return InProgress;
            }

            if (agent.BB.Int1 % 2 == 1) {
                if (Rng.UChance(0.5f)) {
                    if (TryAdmireObject(S, agent, goal, date, window: false)) {
                        return InProgress;
                    }
                }
                if (TryAdmireObject(S, agent, goal, date, window: true)) {
                    return InProgress;
                }
                // failed to find anything to admire
                IncreaseDatePhase(agent);
            }

            return InProgress;
        }

        private void IncreaseDatePhase(AIAgentComp agent)  {
            // D.Warn("Increasing date phase: {0}++", agent.BB.Int1);
            agent.BB.Int1++;
        }

        private bool TryAdmireObject(GameState S, AIAgentComp agent, AIGoal goal,
                Being date, bool window) {
            // try admire an object together
            if (agent.BB.Object1 is AdmirableComp admirable) {
                // FaceTarget(admirable, agent);
                // FaceTarget(admirable, date.AIAgent);
                // TODO time limit, etc
                if (agent.Motion.TicksInCurrentPosition > Consts.TicksPerHour) {
                    // D.Warn("timeout for admire: {0} -> {1}",
                    //     admirable, agent.Motion.TicksInCurrentPosition);
                    IncreaseDatePhase(agent);
                    agent.BB.Object1 = null;
                }
                return true;
            }
            var admireAction = (AIActionFunAdmireObject)Registries.AIActions.FastGet(
                AIActionFunAdmireObject.ActIdH);
            admirable = admireAction.FindAdmirableObject(S, agent,
                chanceNonWindows: window ? 0f : 1f);
            if (admirable == null) {
                // will increase date phase outside the call
                return false;

            }
            agent.BB.Object1 = admirable;
            var admirePos = admirable.AdmirePosIdx(agent);
            if (!agent.Motion.TryGoTo(admirePos, PathReqFlags.AdjOnTop
                    | PathReqFlags.Instant, out agent.BB.MoveProgress)) {
                agent.BB.Object1 = null;
                return false;
            }
            // we will be standing on the same tile with our date
            // so without this flag the agent will try to move aside
            agent.Motion.Request.Flags |= PathReqFlags.TempDestination;
            if (date.Motion.TryGoTo(admirePos,
                PathReqFlags.AdjOnTop | PathReqFlags.Instant
                    | PathReqFlags.TempDestination)) {
                // successfully went to observe
                return true;
            } else {
                // date failed to move together!
                agent.Motion.Abort("Date failed to follow");
                // move to next activity
                agent.BB.Object1 = null;
                return false;
            }
        }

        public static bool EnsureTargetBeing(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BB.TargetBeing != null) {
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.BeingId_Uint, out uint dateBeingId)) {
                var date = S.Query.FindBeingData.Ask(dateBeingId);
                if (date?.Being == null) {
                    return false;
                }
                agent.BB.TargetBeing = date.Being;
                var dateSpeed = date.StatsBeing.MoveSpeed.Value;
                var ourSpeed = agent.BeingData.StatsBeing.MoveSpeed.Value;
                // half of speed of slowest being
                var wantedSpeed = Mathf.Min(dateSpeed, ourSpeed) * 0.5f;
                var mod = StatModifier.Sets(wantedSpeed, T.NounDate);
                if (agent.BeingData.StatsBeing.MoveSpeed.TryAddModifier(mod)) {
                    // remember modifier so we can remove it later
                    agent.BB.Object2 = mod;
                    D.Warn("Setting move speed mod on: {0}", agent);
                } else {
                    D.Err("Failed setting move speed mod on: {0}", agent);
                }
                return true;
            }
            D.Err("Date goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
