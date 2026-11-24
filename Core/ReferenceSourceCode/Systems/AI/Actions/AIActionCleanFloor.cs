using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;

namespace Game.Systems.AI {
    public sealed class AIActionCleanFloor : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionCleanFloor());
        }
        public const string ActId = "CleanFloor";
        public static int ActIdH;
        private AIActionCleanFloor() : base(ActId, () => T.ActCleanfloors) {
            // A bit of a hack, initial world state can't be empty
            // this way this works for both idle cleaning and forced cleaning
            Preconditions = AIState.With(AIVarsH.IsIdle, false);
            Outcomes = AIState.With(AIVarsH.IsFloorClean, true);
            WithRequiredAbilities(AbilityIdH.CleanFloors, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Cleaning);
            ActIdH = IdH;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // current target
            agent.BB.Int1 = 0;
            // for manual forced target dirt
            if (goal.TryGetVar(AIGoalVars.TargetPos, out int pos)) {
                agent.BB.Int1 = pos;
                goal.ClearVars();
            }
            // num cleaned this run
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();

            if (goal.Type == AIGoalType.Idle) {
                goal.WithInterrupt(1, 0);
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Int1 > 0) {
                agent.S.Sys.Dirt.Unreserve(agent.BB.Int1);
            }
            agent.BB.Int1 = 0;
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.HideThoughtBubble();

            if (agent.Being.Data.Species.IsBiological) {
                int cooldownFrom = Consts.TicksPer6Hours;
                int cooldownTo = Consts.TicksPer8Hours;
                if (agent.Jobs.TryJob(JobTypeIdH.Cleaning, out var job)) {
                    var prio = job.Priority;
                    if (prio > 0) {
                        cooldownFrom /= prio;
                        cooldownTo /= prio;
                    }
                }
                if (agent.BeingData.HasTrait(TraitIdH.CleaningOcd)) {
                    agent.BB.SetCooldown(NeedIdH.CleanFloors, ticks, cooldownFrom / 4, cooldownTo / 4);
                } else {
                    agent.BB.SetCooldown(NeedIdH.CleanFloors, ticks, cooldownFrom, cooldownTo);
                }
            } else {
                if (agent.Being.TryGetComponent<CleaningBotComp>(out var cb)) {
                    var timeout = cb.AutoCleanIntervalMinutes * Consts.TicksPerMinute;
                    agent.BB.SetCooldown(NeedIdH.CleanFloors, ticks, timeout, timeout + Consts.TicksPer3Minutes);
                } else {
                    agent.BB.SetCooldown(NeedIdH.CleanFloors, ticks, Consts.TicksPer1Hours, Consts.TicksPer2Hours);
                }
            }

            if (!AIGoal.IsFailed(goal) && goal.Target is ConstructableComp) {
                // it was a tile clean goal. now we can convert it
                // into direct order and keep it that way

                agent.S.Clock.OnNextTick(() => {
                    var forcedGoal = AIGoal.DirectOrder(JobTypeIdH.Cleaning,
                            IconId.CMop, T.CleanFloors, agent, ticks)
                        // A bit of a hack, initial world state can't be empty
                        // this way this works for both idle cleaning and forced cleaning
                        .WithWorld(AIVarsH.IsIdle, false)
                        .WithDesired(AIVarsH.IsFloorClean, true);

                    agent.S.Query.ForceGoal.Ask(forcedGoal, false);
                });
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (S.Sys.Dirt.DirtCount == 0) {
                // D.Warn("No more dirt!");
                goal.WithWorld(AIVarsH.IsFloorClean, true);
                return Success;
            }
            bool isBot = !agent.Being.Species.IsBiological;
            int cleanedCount = agent.BB.Byte1;
            if (agent.BB.Int1 == 0) {
                // check if it's time to stop
                int minCleanTicks;
                int minCleanCount;
                if (isBot) {
                    if (goal.Type == AIGoalType.Idle && agent.Being.TryGetComponent<CleaningBotComp>(out var cb)) {
                        if (!cb.IsAutoCleanEnabled) {
                            goal.WithWorld(AIVarsH.IsFloorClean, true);
                            return Success;
                        }
                    }
                    minCleanCount = 10;
                    minCleanTicks = Consts.TicksPer4Hours;
                } else {
                    minCleanCount = 8;
                    minCleanTicks = Consts.TicksPer3Hours;
                }
                if (cleanedCount > minCleanCount || agent.TicksInCurrentAction > minCleanTicks) {
                    float chance = (cleanedCount - minCleanCount) / (minCleanCount * 2f);
                    // D.Warn("Cleaned: {0}. Stop chance: {1}", cleanedCount, chance);
                    if (Rng.UChance(chance)) {
                        // D.Warn("Cleaned {0} before stopping", cleanedCount);
                        goal.WithWorld(AIVarsH.IsFloorClean, true);
                        return Success;
                    }
                }
                int maxDist;
                if (isBot) {
                    maxDist = 100;
                } else {
                    maxDist = 50;
                }
                if (!S.Sys.Dirt.FindClosestDirtReachableBy(
                        agent.Being, agent.BB.Blacklist, out agent.BB.Int1, maxDist)) {
                    // D.Warn("No more reachable dirt");
                    goal.WithWorld(AIVarsH.IsFloorClean, true);
                    return Success;
                } else {
                    agent.ShowThoughtBubble(IconId.CMop, T.CleaningFloors);
                    S.Sys.Dirt.Reserve(agent.BB.Int1);
                    agent.BB.ClearMoveTarget();
                }
            }

            if (agent.PosIdx == agent.BB.Int1) {
                agent.BB.MoveProgress = null;
                float chance;
                if (isBot) {
                    chance = 0.05f;
                } else {
                    chance = 0.1f;
                }
                if (agent.Jobs.TryJob(JobTypeIdH.Cleaning, out var job)) {
                    chance *= (1f + job.UseSkill()) * job.WorkSpeedMul();
                    // D.Warn("Clean chance: {0}. eff: {1}", chance, job.Efficiency);
                    if (S.Sys.Dirt.CleanOneAt(agent.BB.Int1, out var hadDirt, chance)) {
                        if (hadDirt) {
                            agent.BB.Byte1++;
                        }
                        // if player force assigns this task, needs will still be able to
                        // interrupt it
                        if (goal.IsForceAssigned) {
                            // normally it would be 3
                            goal.WithInterrupt(1, 0);
                            goal.Flags &= ~AIGoalFlags.ForceAssigned;
                        }
                        if (goal.Target is ConstructableComp) {
                            goal.WithWorld(AIVarsH.IsFloorClean, true);
                            return Success;
                        } else {
                            var floor = EntityUtils.FloorAt(agent.PosIdx);
                            if (floor?.Goal?.JobIdH == JobTypeIdH.Cleaning) {
                                // we finished someone else's cleaning goal
                                AIGoal.Cancel(floor.Goal, G.Ticks,
                                    reason: T.AdjDone, removeImmediately: false);
                            }
                        }
                        // try find nearby dirt?
                        var nextDirtyPos = Pos.Invalid;
                        Pos.ForEachAroundRandom(S.Rng, agent.PosIdx, 1,
                                false, true, (p) => {
                            if (nextDirtyPos == Pos.Invalid) {
                                if (S.Sys.Dirt.IsDirty(p) && agent.IsFeelingSafeAt(p)) {
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
            } else {
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

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 0.0001f;
        }
    }
}
