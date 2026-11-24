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
using Game.UI;
using Game.Systems.Reg;
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public sealed class AIActionIdleRoam : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionIdleRoam());
        }
        public static StatModifier StatModRoam = StatModifier.AddPercent(-0.5f, T.ActRoam);

        // Register runs before the language loads, which left the label in English. The field
        // stays as it is (mods bind to it), so its label is set again once the language is in.
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void LocalizeRoamMod() {
            StatModRoam.NameT = T.ActRoam;
        }

        public const string ActId = "IdleRoam";
        private AIActionIdleRoam() : base(ActId, () => T.ActRoam) {
            Preconditions = AIState.With(AIVarsH.IsIdle, false);
            Outcomes = AIState.With(AIVarsH.IsIdle, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            if (goal.Type == AIGoalType.Idle) {
                goal.WithInterrupt(0, 0);
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            agent.Being.Data.StatsBeing.MoveSpeed.RemoveModifier(StatModRoam, false);
        }

        private static bool CanRoamTo(int pos, bool isCurrentSafe,
                bool isCurrentSurvivable, bool allowUnsafe, BeingSafety safety) {
            if (!allowUnsafe) {
                if (isCurrentSafe) {
                    return safety.IsActuallySafeAt(pos);
                }
                if (isCurrentSurvivable) {
                    return safety.IsActuallySurvivableAt(pos);
                }
                return false;
            }
            // unsafe ok
            if (isCurrentSurvivable) {
                return safety.IsActuallySurvivableAt(pos);
            }
            return true;

        }

        public static bool TryRoam(AIAgentComp agent, bool addSpeedMod,
                int numRegions, bool allowUnsafe = false, bool instant = false) {
            if (agent.Motion.SkipRunAgent) {
                return false;
            }
            var safety = agent.BeingData.Safety;
            if (safety == null) {
                D.Err("Safety is null when trying to roam! Ticks in cur act: {0} | {1} | {2} | {3} | {4}",
                    Units.TicksToTime(agent.TicksInCurrentAction), agent.Motion.Position,
                        agent, agent.Being, agent.BeingData);
                return false;
            }
            if (agent.BB == null) {
                D.Err("BB is null when trying to roam! {0}", agent);
                return false;
            }
            var isG = agent.Motion.IsGrounded;
            bool allowFly = !isG && agent.RoamFailCount > 0;
            var sys = agent.S.Sys;
            var sig = agent.S.Sig;
            var query = agent.S.Query;
            if (agent.Motion.IsBusy) {
                D.Err("Activating IdleRoam while still in motion: {0} {1}",
                    agent.Motion.Result, agent);
                agent.Motion.Fail("IdleRoam!");
            }
            if (agent.PosIdx < 1) {
                D.Err("Agent trying to roam from <1 pos! Ticks in cur act: {0} | {1} | {2} | {3} | {4}",
                    Units.TicksToTime(agent.TicksInCurrentAction), agent.Motion.Position,
                        agent, agent.Being, agent.BeingData);
                return false;
            }
            var isSafe = safety.IsActuallySafeAt(agent.PosIdx);
            var isSurvivable = isSafe || safety.IsActuallySurvivableAt(agent.PosIdx);
            if (!isSurvivable) {
                // Where we stand is already killing us, so every candidate would be
                // rejected and we'd be stuck here. Any way out beats staying.
                allowUnsafe = true;
            }
            // check if we are by chance inside a structure
            var obj = EntityUtils.GetTopmostParentTile(agent.Motion.Position.Idx);
            if (obj != null && !obj.Passability.IsPassable) {
                // we are inside something, let's try to find a way out
                var unstuckPos = Pos.Invalid;
                Pos.ForEachInRadius(obj.CenterIdx, obj.Size.magnitude + 1f, false, (p) => {
                    if ((allowFly ? sys.Path.IsPassable(p) : sys.Path.IsNavigable(p, isG))
                            && (allowUnsafe || safety.IsActuallySafeAt(p))) {
                        unstuckPos = p;
                        return false;
                    }
                    return true;
                });
                if (unstuckPos != Pos.Invalid) {
                    if (agent.Motion.TryGoTo(unstuckPos, PathReqFlags.AdjOnTop,
                            out agent.BB.MoveProgress)) {
                        agent.ClearRoamFailure();
                        return true;
                    }
                    D.ErrSoft("Could not go to unstuck pos: {0} ({1})-> {2}",
                        Pos.String(unstuckPos), agent.BB.MoveProgress.FailReason, agent);
                }
            }
            // unstuck harder
            if (agent.RoamFailCount > 0) {
                numRegions++;
            }
            var path = sys.Path;
            var roamable = agent.RoamFailCount == 0;
            var wantedPos = query.FindNearbyRandom.Ask(agent.PosIdx,
                isG, numRegions, (p) => {
                    return (allowFly ? path.IsPassable(p) : (roamable
                        ? path.IsRoamable(p, isG) : path.IsNavigable(p, isG)))
                            && CanRoamTo(p, isSafe, isSurvivable, allowUnsafe, safety);
                });
            if (wantedPos != Pos.Invalid) {
                // D.Warn("roam found nearby {0} for {1}",
                //     Pos.StringFromTo(agent.Motion.Position.Idx, wantedPos), agent);
                var roamFlags = PathReqFlags.AdjOnTop;
                if (allowUnsafe) {
                    roamFlags |= PathReqFlags.UnsafeOK;
                }
                if (instant) {
                    roamFlags |= PathReqFlags.Instant;
                }
                if (agent.Motion.TryGoTo(wantedPos, roamFlags,
                        out agent.BB.MoveProgress)) {
                    if (addSpeedMod) {
                        agent.Being.Data.StatsBeing.MoveSpeed.AddModifier(StatModRoam, true);
                    }
                    agent.ClearRoamFailure();
                    return true;
                } else {
                    if (agent.AddRoamFailure()) {
                        D.Warn("Could not roam to: {0} ({1})-> {2} | NumRegions: {3}, CurReg: {4}",
                            Pos.String(wantedPos), agent.BB.MoveProgress.FailReason, agent,
                            numRegions, query.RegionAt.Ask(agent.PosIdx));
                    }
                }
            } else {
                if (agent.AddRoamFailure()) {
                    if (agent.Motion.IsGrounded &&
                            agent.Motion.HasFlags(Motion.MotionFlags.InSpace)) {
                        agent.Motion.Immobilize(ImmobilizeReasonIdH.GroundedInSpace, true);
                        return false;
                    }
                    var sb = Caches.StringBuilder3;
                    sb.Clear();
                    var s = agent.S;
                    var syst = s.Sys.Get<RegSys>(RegSys.SysId);
                    RegViz.AddRegInfoTip(s, syst, sb, agent.PosIdx, out _, out _);
                    D.ErrSoft("Could not find where to roam to: {0}. {1}. Topmost tile: {2}. NumReg: {3}. CurReg: {4}. Reg Debug: {5}",
                        agent, Pos.String(agent.PosIdx),
                            EntityUtils.GetTopmostParentTile(agent.PosIdx),
                            numRegions, query.RegionAt.Ask(agent.PosIdx), sb);
                    agent.Motion.TryUnstuck(agent.Motion.IsGrounded);
                    #if !PRODUCTION_BUILD
                    EntityUtils.DebugPauseFocusOn(agent.Being, "Agent can't roam: {0}");
                    #endif
                }
            }
            return false;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if ((agent.BeingData.EnvFlags & Creatures.BeingEnvFlags.Safe)
                        != Creatures.BeingEnvFlags.Safe) {
                    // We're unsafe! let's rush a bit
                    agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(
                        StatModRoam, false);
                }
                if (agent.IsFeelingSafeNow || agent.Motion.Progress.IsUnsafe) {
                    return InProgress;
                }
            }
            if (agent.BB.MoveProgress == null) {
                int numRegions;
                // 20% chance to wander a bit further away
                if (Rng.UChance(0.2f)) {
                    numRegions = 2;
                } else {
                    numRegions = 1;
                }
                if (TryRoam(agent, true, numRegions)) {
                    return InProgress;
                } else {
                    agent.BB.SetCooldown(IdH, ticks,
                        Consts.TicksPer3Minutes, Consts.TicksPer5Minutes);
                }
            }
            goal.WorldState.Set(AIVarsH.IsIdle, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            // task on being, possibly bring to surgery, don't take work
            if (agent.Being.Goal?.JobIdH == JobTypeIdH.Task) { return 0f; }
            if (agent.Motion.CanIdle) {
                if (agent.Being.Species.IsBiological) {
                    if (agent.Being.Motion.TicksInCurrentPosition > Consts.TicksPerHour) {
                        // GTFO
                        return 0.00001f;
                    }
                    return 0.5f + Rng.UValue;
                }
                return 0.8f + Rng.UValue;
            } else {
                // We must go
                return 0.0001f;
            }
        }
    }
}
