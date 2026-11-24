using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.AI;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionCleaningSpree : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionCleaningSpree());
        }
        public const string ActId = "BreakdownCleaningSpree";
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.BreakdownCleaningSpreeDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalWentOnCleaningSpree;
        private AIBreakdownActionCleaningSpree() : base(ActId, () => T.BreakdownCleaningSpree) {
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.CleanFloors);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            // check if it's time to stop
            if (agent.TicksInCurrentAction > Consts.TicksPer24Hours) {
                // D.Warn("Cleaned: {0}. Stop chance: {1}", cleanedCount, chance);
                if (Rng.UChance(0.01f)) {
                    return EndBreakdown(agent, goal);
                }
            }
            if (S.Sys.Dirt.DirtCount == 0) {
                return FoundNoDirt(S, agent, goal, ticks);
            }
            if (agent.BB.Int1 == 0) {
                if (!S.Sys.Dirt.FindClosestDirtReachableBy(
                        agent.Being, agent.BB.Blacklist, out agent.BB.Int1, 100)) {
                    return FoundNoDirt(S, agent, goal, ticks);
                } else {
                    // last time we found dirt
                    agent.BB.Long1NotSaved = ticks;
                    S.Sys.Dirt.Reserve(agent.BB.Int1);
                    agent.BB.ClearMoveTarget();
                }
            }

            if (agent.PosIdx == agent.BB.Int1) {
                agent.BB.MoveProgress = null;
                float chance = 0.1f;
                if (agent.Jobs.TryJob(JobTypeIdH.Cleaning, out var job)) {
                    chance *= (1f + job.UseSkill()) * job.WorkSpeedMul();
                    // D.Warn("Clean chance: {0}. eff: {1}", chance, job.Efficiency);
                    if (S.Sys.Dirt.CleanOneAt(agent.BB.Int1, out var hadDirt, chance)) {
                        // try find nearby dirt?
                        var nextDirtyPos = Pos.Invalid;
                        Pos.ForEachAroundRandom(S.Rng, agent.PosIdx, 1,
                                false, true, (p) => {
                            if (nextDirtyPos == Pos.Invalid) {
                                if (S.Sys.Dirt.IsDirty(p)) {
                                    nextDirtyPos = p;
                                    return false;
                                }
                                return true;
                            }
                            return false;
                        });
                        if (nextDirtyPos != Pos.Invalid) {
                            agent.BB.Int1 = nextDirtyPos;
                            S.Sys.Dirt.Reserve(nextDirtyPos);
                        } else {
                            agent.BB.Int1 = 0;
                        }
                    }
                }
            } else if (agent.BB.Int1 > 0) {
                if (!TryGoToOnce(agent, agent.BB.Int1, false)) {
                    S.Sys.Dirt.Unreserve(agent.BB.Int1);
                    agent.BB.AddToBlacklist(agent.BB.Int1);
                    agent.Motion.StoreNavFailureAt(agent.BB.Int1);
                    agent.BB.Int1 = 0;
                    agent.BB.MoveProgress = null;
                    return InProgress;
                }
            }

            return InProgress;
        }

        #if PRODUCTION_BUILD
        public const int MaxWait = Consts.TicksPer3Hours;
        #else
        // for tests
        public static int MaxWait = Consts.TicksPer3Hours;
        #endif

        private AIActionResult FoundNoDirt(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Int1 = 0;
            if (ticks - agent.BB.Long1NotSaved > MaxWait) {
                // couldn't find dirt for 3 hours
                return EndBreakdown(agent, goal);
            }
            // fall back to roaming
            if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true)) {
                return InProgress;
            } else {
                var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                if (pos != Pos.Invalid) {
                    agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK);
                }
                return InProgress;
            }
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // current target
            agent.BB.Int1 = 0;
            // last tick when we found dirt
            agent.BB.Long1NotSaved = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Int1 != 0) {
                agent.S.Sys.Dirt.Unreserve(agent.BB.Int1);
            }
            agent.BB.Int1 = 0;
            // last tick when we found dirt
            agent.BB.Long1NotSaved = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
