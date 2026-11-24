using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;

namespace Game.Systems.AI {
    // Works a hidden per-room cleaning task (DirtCleaningTasks): cleans only inside the
    // task's room or outside area, and hands the task back every now and then, because the
    // lowest priority work must not keep a worker from new work for long.
    // Why it works this way: Docs/Architecture/Systems/AI/CleaningTasks.md
    public sealed class AIActionCleanArea : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionCleanArea());
        }
        public const string ActId = "CleanArea";
        // How long a worker cleans before it hands the task back. With nothing more
        // important around it wins the task again on the next auction
        private const int VisitTicks = Consts.TicksPer30Minutes;
        // Straight line reach of the search for the next dirt, which looks in the room the
        // worker stands in before going through doors
        private const int SearchDist = 100;

        private AIActionCleanArea() : base(ActId, () => T.ActCleanfloors) {
            Preconditions = AIState.With(AIVarsH.IsAreaClean, false);
            Outcomes = AIState.With(AIVarsH.IsAreaClean, true);
            WithRequiredAbilities(AbilityIdH.CleanFloors, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Cleaning);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // target dirt
            agent.BB.Int1 = 0;
            // tiles cleaned this visit
            agent.BB.Byte1 = 0;
            // walked to the task's anchor to search from inside the area
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Int1 > 0) {
                agent.S.Sys.Dirt.Unreserve(agent.BB.Int1);
            }
            agent.BB.Int1 = 0;
            agent.BB.Byte1 = 0;
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.HideThoughtBubble();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            var anchor = goal.ReachEntity;
            if (anchor == null) { return 0f; }
            if (agent.Being.TryGetComponent<CleaningBotComp>(out var bot)
                    && !bot.IsAutoCleanEnabled) {
                // Auto Clean off means the bot only cleans when told to. The cooldown keeps
                // it from planning the task again on every tick it stands idle
                agent.BB.SetGoalCooldown(goal.Id, ticks, Consts.TicksPer10Minutes,
                    Consts.TicksPer15Minutes);
                return 0f;
            }
            // job priority, skill and distance pick the worker, as for any colony task
            return base.CostFor(agent, goal, ticks) + agent.Motion.GoToCost(anchor.PosIdx);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var dirt = S.Sys.Dirt;
            var tasks = dirt.CleaningTasks;
            var area = tasks.AreaOfTask(goal);
            if (area == 0) {
                // the floor the task was anchored on is gone; a new task comes for the
                // area if it is still dirty
                return Finish(goal, tasks);
            }
            if (agent.BB.Int1 == 0) {
                if (IsVisitOver(agent)) {
                    return HandBack(agent, goal, tasks);
                }
                if (!dirt.FindClosestDirtReachableBy(agent.Being, agent.BB.Blacklist,
                        out var found, SearchDist, area)) {
                    if (!tasks.HasActualDirt(area)) {
                        return Finish(goal, tasks);
                    }
                    if (!agent.BB.Bool1 && tasks.AreaOf(agent.PosIdx) != area) {
                        // the area may lie beyond the search, look again from inside it
                        agent.BB.Bool1 = true;
                        agent.BB.ClearMoveTarget();
                        if (TryGoToOnce(agent, goal.ReachEntity.PosIdx, false)) {
                            return InProgress;
                        }
                    }
                    // what is left here is out of this worker's reach, someone else may
                    // get to it
                    agent.BB.SetGoalCooldown(goal.Id, ticks, Consts.TicksPer10Minutes,
                        Consts.TicksPer15Minutes);
                    return FailedSoft(T.AdRejUnreachable);
                }
                agent.BB.Int1 = found;
                dirt.Reserve(found);
                agent.BB.ClearMoveTarget();
                agent.ShowThoughtBubble(IconId.CMop, T.CleaningFloors);
            }

            var target = agent.BB.Int1;
            if (agent.PosIdx != target) {
                if (!TryGoToOnce(agent, target, false)) {
                    dirt.Unreserve(target);
                    agent.BB.AddToBlacklist(target);
                    agent.Motion.StoreNavFailureAt(target);
                    agent.BB.Int1 = 0;
                    agent.BB.MoveProgress = null;
                }
                return InProgress;
            }

            agent.BB.MoveProgress = null;
            if (!agent.Jobs.TryJob(JobTypeIdH.Cleaning, out var job)) {
                return FailedSoft(T.GoalWorkerIncapable);
            }
            var chance = agent.Being.Species.IsBiological ? 0.1f : 0.05f;
            chance *= (1f + job.UseSkill()) * job.WorkSpeedMul();
            if (!dirt.CleanOneAt(target, out var hadDirt, chance)) {
                return InProgress;
            }
            if (hadDirt && agent.BB.Byte1 < byte.MaxValue) {
                agent.BB.Byte1++;
            }
            // someone else may have cleaned it first, which leaves the reservation behind
            dirt.Unreserve(target);
            agent.BB.Int1 = 0;
            // in a filthy room there is always dirt next door, so the hand back is checked
            // after every tile, not only when searching
            if (IsVisitOver(agent)) {
                return HandBack(agent, goal, tasks);
            }
            var next = NextDirtAround(dirt, tasks, agent, area);
            agent.BB.Int1 = next;
            if (next != 0) {
                dirt.Reserve(next);
            }
            return InProgress;
        }

        // Lets the auction see whether anything more important waits for this worker, once
        // it has cleaned something on this visit
        private static bool IsVisitOver(AIAgentComp agent) {
            return agent.BB.Byte1 > 0 && agent.TicksInCurrentAction >= VisitTicks;
        }

        // With nothing more important around, the worker wins its own room back (STA-1740)
        private AIActionResult HandBack(AIAgentComp agent, AIGoal goal, DirtCleaningTasks tasks) {
            tasks.Reanchor(goal, agent.PosIdx);
            return PutBack(null);
        }

        // The tile right next to the cleaned one first, before searching the area again
        private static int NextDirtAround(DirtSys dirt, DirtCleaningTasks tasks,
                AIAgentComp agent, int area) {
            var around = Caches.PosIdx9;
            int count = Pos.Around(agent.PosIdx, around, false);
            for (int i = 0; i < count; i++) {
                var p = around[i];
                if (dirt.IsDirty(p) && !dirt.IsReserved(p) && agent.IsFeelingSafeAt(p)
                        && tasks.AreaOf(p) == area) {
                    return p;
                }
            }
            return 0;
        }

        private AIActionResult Finish(AIGoal goal, DirtCleaningTasks tasks) {
            goal.WithWorld(AIVarsH.IsAreaClean, true);
            // dirt that came back meanwhile gets a new task on the next sync
            tasks.MarkSync();
            return Success;
        }
    }
}
