// #define DEBUG_AI_PLANNER
// #define AI_PLANNER_FORWARD
#if !PRODUCTRION_BUILD
// #define DEBUG_AI_BATCHING
// #define DEBUG_COMBAT
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.Mental;
using Game.Tools;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using Priority_Queue;
using UnityEngine;
using Game.Systems.Creatures;
using Game.UI;
using Game.Systems.UIUX;
using Game.Systems.AI.Combat;

namespace Game.Systems.AI {
    public sealed class AISys : GameSystem, ISaveableSpecial {

        #if PRODUCTION_BUILD
        private const float personalGoalBatchSize = 32f;
        #else
        public static float personalGoalBatchSize = 32f;
        #endif

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AISys());
            #if !PRODUCTION_BUILD
            personalGoalBatchSize = 32f;
            #endif
        }
        private const int maxGoalQueue = 64;
        // A goal can wait in Bidding for hours while the world changes under its doability
        // gate (storage fills up, material gets claimed), so a waiting goal re-runs the gate
        // this often. Capped per tick because some gates scan every storage and pile.
        // See Docs/Architecture/Systems/AI/TaskFailureHandling.md
        internal const int GateRecheckTicks = Consts.TicksPer15Minutes;
        internal const int MaxGateRechecksPerTick = 8;
        internal int GateRechecksThisTick;
        #if !PRODUCTION_BUILD
        // Drafted agents that took part in goal selection last tick (attack orders only)
        public int LastDirectOrderAgents;
        public readonly AIAuctionStats Stats = new();
        #endif
        // Colony goals of each priority, filled in one walk per tick and processed highest
        // priority first (gates that claim materials see the more important goals first).
        // Walking the whole dictionary once per priority level cost more than processing
        // the goals with thousands of hauling goals around
        private readonly List<AIGoal>[] goalsByPriority = {
            new(), new(), new(), new(), new(), new(),
        };
        // This tick's bidding goals by tier and job, for the auction
        private readonly AIAuctionIndex auctionIndex = new();
        // set when the auction's lazy admission changed a goal's state
        private bool admitChanged;
        // Checking every goal's target for fire chases several pointers per goal, so it
        // is skipped while nothing burns at all
        private bool isAnyFlammableOnFire;
        public const string SysId = "AISys";
        public override string Id => SysId;
        private readonly HashSet<int> warnedAboutNoWorkerJobTypeIds = new();

        private readonly Dictionary<uint, AIGoal> colonyGoals = new();
        private readonly FastPriorityQueue<AIGoal> goalQueue = new(maxGoalQueue);
        private List<AIGoal> goalsWithError;
        public List<AIGoal> GoalsWithError => goalsWithError;
        private readonly HashSet<AIGoal> constructionGoals = new();
        public Dictionary<uint, AIGoal> ColonyGoals => colonyGoals;
        private readonly List<AIAgentComp> agents = new();
        public List<AIAgentComp> Agents => agents;
        private IAIPlanner planner;
        private AIAuction auction;
        private UnreachableTasks unreachable;
        internal UnreachableTasks Unreachable => unreachable;

        public override void Unload() {
            agents.Clear();
            goalsWithError?.Clear();
            colonyGoals.Clear();
            goalQueue.Clear();
            planner = null;
            auction = null;
            unreachable = null;
        }

        protected override void OnInitialize() {
            AIGoal.LastId = 0;
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.AIAddGoal.AddListener(OnAddGoal);
            S.Sig.AIRemoveGoal.AddListener(OnRemoveGoal);
            S.Sig.AIAddAgent.AddListener(OnAddAgent);
            S.Sig.AIRemoveAgent.AddListener(OnRemoveAgent);
            S.Sig.IslandCountChanged.AddListener(OnIslandCountChanged);
            S.Sig.BeforeSave.AddListener(OnBeforeSave);
            S.Query.ForceGoal.SetResponder(TryForceGoal);
            S.Query.AIGoalsAtPos.SetResponder(FindGoalsAtPos);
            S.Query.AIGoalsInArea.SetResponder(FindGoalsInArea);
            S.Query.HasAIGoalAtPos.SetResponder(HasAIGoalAtPos);
            S.Query.AIGoalsByType.SetResponder(FindGoalsByType);
        }

        private bool FindGoalsByType(int jobTypeIdH, List<AIGoal> result) {
            bool found = false;
            foreach (var goal in colonyGoals.Values) {
                if (goal.Job.IdHash == jobTypeIdH) {
                    result.Add(goal);
                    found = true;
                }
            }
            return found;
        }

        private bool HasAIGoalAtPos(int pos) {
            var obj = EntityUtils.ParentObjAt(pos);
            if (obj?.Goal != null) {
                return true;
            }
            var wall = EntityUtils.WallAt(pos);
            if (wall?.Goal != null) {
                return true;
            }
            var floor = EntityUtils.FloorAt(pos);
            if (floor?.Goal != null) {
                return true;
            }
            Caches.ObjList.Clear();
            int foundObjs = S.Objs.FindVisualInRect(
                Pos.ToVector(pos, 0f, 0f),
                Pos.ToVector(pos, 1f, 1f),
                Caches.ObjList, (o) => o.Goal != null);
            if (foundObjs > 0) {
                return true;
            }
            Caches.ObjList.Clear();
            Caches.BeingList.Clear();
            int foundBeings = S.Beings.FindVisualInRect(
                Pos.ToVector(pos, 0f, 0f),
                Pos.ToVector(pos, 1f, 1f),
                Caches.BeingList, (o) => o.Goal != null);
            if (foundBeings > 0) {
                return true;
            }
            Caches.BeingList.Clear();
            return false;
        }

        private int FindGoalsAtPos(int pos, List<AIGoal> res) {
            int found = 0;
            var obj = EntityUtils.ParentObjAt(pos);
            if (obj?.Goal != null) {
                found++;
                res.Add(obj.Goal);
            }
            var wall = EntityUtils.WallAt(pos);
            if (wall?.Goal != null) {
                found++;
                res.Add(wall.Goal);
            }
            var floor = EntityUtils.FloorAt(pos);
            if (floor?.Goal != null) {
                found++;
                res.Add(floor.Goal);
            }
            found += AddRelocationGoal(obj, res);
            found += AddRelocationGoal(wall, res);
            found += AddRelocationGoal(floor, res);
            Caches.ObjList.Clear();
            int foundObjs = S.Objs.FindVisualInRect(
                Pos.ToVector(pos, 0f, 0f),
                Pos.ToVector(pos, 1f, 1f),
                Caches.ObjList, (o) => o.Goal != null);
            for (int i = 0; i < Caches.ObjList.Count; i++) {
                var o = Arrays.SafeGetFast(Caches.ObjList, i);
                if (o != null && o.Goal != null) {
                    found++;
                    res.Add(o.Goal);
                }
            }
            Caches.ObjList.Clear();
            Caches.BeingList.Clear();
            int foundBeings = S.Beings.FindVisualInRect(
                Pos.ToVector(pos, 0f, 0f),
                Pos.ToVector(pos, 1f, 1f),
                Caches.BeingList, (o) => o.Goal != null);
            for (int i = 0; i < Caches.BeingList.Count; i++) {
                var b = Arrays.SafeGetFast(Caches.BeingList, i);
                if (b != null && b.Goal != null) {
                    found++;
                    res.Add(b.Goal);
                }
            }
            Caches.BeingList.Clear();
            Caches.EntityProxyList.Clear();
            int foundProxies = S.Query.GetEntityProxiesAt.Ask(pos, Caches.EntityProxyList);
            if (foundProxies > 0) {
                for (int i = 0; i < foundProxies; i++) {
                    var pr = Caches.EntityProxyList[i];
                    if (pr.Entity.Goal != null) {
                        found++;
                        res.Add(pr.Entity.Goal);
                    }
                }
            }
            return found;
        }

        // A relocation's task lives on the ghost at the destination, which can be far away
        // from the object the player sees and clicks
        private static int AddRelocationGoal(Tile source, List<AIGoal> res) {
            var reinstallable = source?.Reinstallable;
            if (reinstallable == null || reinstallable.TargetId == 0) { return 0; }
            if (reinstallable.Target?.TryGetGoal(out var goal) != true) { return 0; }
            if (res.Contains(goal)) { return 0; }
            res.Add(goal);
            return 1;
        }

        private static bool IsRelocationSourceIn(AIGoal goal, PosRect area) {
            if (goal.Target is not ReinstallableComp ghost) { return false; }
            var source = ghost.Source?.Tile;
            return source != null && source.IsActive && area.Contains(source.PosIdx);
        }

        private int FindGoalsInArea(PosRect area, List<AIGoal> res) {
            int found = 0;
            Caches.EntityProxyList.Clear();
            int foundProxies = S.Query.GetEntityProxiesInRect.Ask(
                Pos.ToVector(area.Min, 0, 0),
                Pos.ToVector(area.Max, 1, 1),
                    Caches.EntityProxyList);
            Caches.IntSet.Clear();
            if (foundProxies > 0) {
                for (int i = 0; i < foundProxies; i++) {
                    var pr = Caches.EntityProxyList[i];
                    Caches.IntSet.Add(pr.Entity.Id);
                }
            }
            foreach (var g in colonyGoals.Values) {
                if (g.Target == null) { continue; }
                if (!g.Target.Entity.IsActive) {
                    // Legit, e.g. a haul whose pile was picked up stays in progress until
                    // delivery (AIActionStoreMat). The Tasks tool asks this on every hover
                    // refresh, so it must not log
                    continue;
                }
                if (area.Contains(g.Target.Entity.PosIdx)
                        || Caches.IntSet.Contains(g.Target.Entity.Id)
                        || IsRelocationSourceIn(g, area)) {
                    found++;
                    res.Add(g);
                }
            }
            return found;
        }

        private bool TryForceGoal(AIGoal goal, bool showFailTooltip) {
            try {
                ProcessGoalPreAssigned(goal, G.Ticks, showFailTooltip);
                return goal.State == AIGoalState.InProgress;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Caught: failed to try force goal: {0}", goal);
                if (showFailTooltip) {
                    The.SysSig.ShowTooltip.Send("Error while trying to assign a task, please report a bug!", true);
                }
                return false;
            }
        }

        private void OnLoadState(GameState state) {
            #if AI_PLANNER_FORWARD
            planner = new AIPlannerForward();
            #else
            planner = new AIPlannerReverse();
            #endif
            auction = new AIAuction(planner) {
                Admit = AdmitGoal,
            };
            #if !PRODUCTION_BUILD
            auction.Stats = Stats;
            #endif
            unreachable = new UnreachableTasks();
            state.Query.GetAIPlanner.SetResponder(GetAIPlanner);
            RestoreAfterLoad();
            // Ready.WhenGameLate(this, "RestoreAgents", () => {
            state.Clock.ExecuteAfterTicks(1, () => {
                state.Clock.OnTick.AddListener(OnTick);
            });
        }

        private IAIPlanner GetAIPlanner() {
            return planner;
        }

        private void RestoreAfterLoad() {
            D.Ass(colonyGoals.Count == 0, "Goals count not 0 when restoring after load! {0}",
                D.Dump(colonyGoals));
            if (data == null) { return; }
            if (data.Goals != null) {
                foreach (var data in data.Goals) {
                    var g = AIGoalData.Deserialize(S, data, false);
                    // A task finished outside the AI tick (a nanobot delivery, cleaning, a
                    // repair) waits here until the next tick drops it, and a save in between
                    // stores it. Its target may already hold the task that followed it, which
                    // this one would push off the target on load (and that task then gets
                    // cancelled as foreign to it), so a finished task is not restored.
                    if (g != null && (g.State == AIGoalState.Completed
                            || g.State == AIGoalState.Cancelled)) {
                        continue;
                    }
                    if (g != null) {
                        colonyGoals.Add(g.Id, g);
                        AddToConstructionGoals(g);
                        if (g.Target?.Entity?.LoadGoal(g) == true) {
                            g.Target.OnGoalLoad(g);
                        }
                    }
                }
            }
            if (data.Agents != null) {
                foreach (var a in agents) {
                    if (data.Agents.TryGetValue(a.EntityId, out var agentData)) {
                        AIAgentData.Deserialize(S, a, agentData, colonyGoals);
                        data.Agents.Remove(a.EntityId);
                        if (a.Goal != null) {
                            a.ShowThoughtBubbleFor(a.Goal);
                        }
                    }
                }
                if (data.Agents.Count != 0) {
                    D.Err("Not all agents were loaded!: {0}",
                        D.Dump(data.Agents));
                }
            }
            #if !PRODUCTION_BUILD
            if (data.GoalsWithError != null) {
                goalsWithError = new();
                foreach (var data in data.GoalsWithError) {
                    var g = AIGoalData.Deserialize(S, data, true);
                    if (g != null) {
                        goalsWithError.Add(g);
                    }
                }
            }
            #endif
            S.Query.FindAIGoal.SetResponder(FindGoal);
            Ready.Set(WhenH.AIAgentsLoaded, true);
        }

        private AIGoal FindGoal(uint id, bool searchPersonalGoals) {
            if (colonyGoals.TryGetValue(id, out var goal)) {
                return goal;
            }
            if (searchPersonalGoals) {
                foreach (var a in agents) {
                    foreach (var pg in a.Goals) {
                        if (pg.Id == id) {
                            return pg;
                        }
                    }
                }
            }
            if (goalsWithError != null) {
                for (int i = goalsWithError.Count - 1; i >= 0; i--) {
                    AIGoal g = goalsWithError[i];
                    if (g.Id == id) {
                        return g;
                    }
                }
            }
            return null;
        }

        private bool isColonyGoalsLocked;

        private readonly HashSet<int> colonyJobTypes = new();
        private readonly List<uint> goalsToRemove = new();
        // An agent or goal that throws is isolated and throws again next tick, so its
        // log line is throttled per agent / goal
        private readonly ErrorThrottle errorThrottle = new();
        // One agent that throws while being prepared must not stop the rest of the
        // agents, or the whole AI tick. Returns early where the loop used to continue.
        private void PrepareAgent(AIAgentComp agent, int i, long ticks, int tickMod,
                int agentBatches) {
            if (agent.Being.IsDead) {
                agent.RemoveAgent();
                return;
            }
            if (agent.BB == null) {
                D.Err("Agent has no Blackboard! Creating now for {0}", agent);
                agent.BB = new();
                agent.BB.SetAgent(agent);
            }
            if (agent.BeingData.Safety == null) {
                D.Err("Agent has no Safety! Creating now for {0}", agent);
                agent.BeingData.Safety = new BeingSafety(S, agent.BeingData);
            }
            if (agent.BeingData.SkipSimulate) {
                return;
            }
            // Only the colony's own beings: a guest's or an enemy's jobs made a task that
            // only they could do wait in bidding forever, instead of failing with "No
            // worker". Colonists whose conditions keep them off work for now still count.
            if (agent.BeingData.IsPlayer) {
                agent.Jobs.AppendDoable(colonyJobTypes);
            }
            var isUnderDC = agent.DirectControl.IsUnderDirectControl;
            if (!agent.CanWork) {
                // D.Warn("Skipping agent due to forced walk: {0}", agent);
                return;
            }
            if (agent.Goal?.IsNonInterruptable == true) {
                return;
            }
            if (isUnderDC) {
                // a drafted unit takes no goals of its own, only the attack order it was given
                if (!agent.CombatUnit.HasDirectAttackOrder) {
                    return;
                }
                #if !PRODUCTION_BUILD
                LastDirectOrderAgents++;
                #endif
            }
            // a bit fugly, but when we try to assign tasks to someone mid teleportation, pathfinding sucks ass
            if (agent.Motion.HasProgressFlag(Motion.MotionProgressFlags.Teleporting)) {
                return;
            }
            // Idle agents (e.g. just finished a task) always re-evaluate personal
            // goals so urgent needs can be wedged in between streamlined,
            // back-to-back tasks instead of waiting on the batching/throttle.
            bool doPersonalGoalCheck = agent.IsIdle || tickMod == 0 || i % agentBatches == tickMod;
            if (doPersonalGoalCheck) {
                FindTopPersonalGoalFor(isUnderDC, agent, ticks);
            } else {
                agent.TopPersonalGoal = null;
            }
            if (!isUnderDC && agent.IsIdle) {
                // Non-player beings never take colony work
                if (agent.CanWorkOnColonyGoals) {
                    auction.AddAgent(agent);
                }
            } else {
                CheckPersonalGoal(ticks, agent);
            }
        }

        private void OnTick(long ticks) {
            // 1. Get ready for a new auction, cleanup old state
            // This auction is for our faction only
            auction.Prepare();
            colonyJobTypes.Clear();
            GateRechecksThisTick = 0;
            #if !PRODUCTION_BUILD
            LastDirectOrderAgents = 0;
            Stats.BeginTick();
            #endif

            if (ticks % Consts.TicksPer3Days == 0) {
                warnedAboutNoWorkerJobTypeIds.Clear();
                // it holds agents and goals as keys, don't let it keep them forever
                errorThrottle.Clear();
                auction.Errors.Clear();
            }

            if (S.Query.HasWorkingNanobots.Ask()) {
                foreach (var jt in JobType.All) {
                    if (jt.Value.NanobotCompatible
                            && S.Query.IsNanobotsJobEnabled.Ask(jt.Key)) {
                        colonyJobTypes.Add(jt.Key);
                    }
                }
            }

            var numAgents = agents.Count;
            int agentBatches = Mathf.CeilToInt(numAgents / personalGoalBatchSize);
            int tickMod = 0;
            if (agentBatches > 1) {
                tickMod = ((int) ticks) % agentBatches;
            }

            for (int i = agents.Count - 1; i >= 0; i--) {
                if (i >= agents.Count) { continue; }
                AIAgentComp agent = agents[i];
                try {
                    PrepareAgent(agent, i, ticks, tickMod, agentBatches);
                } catch (Exception ex) {
                    if (errorThrottle.ShouldLog(agent, out var count)) {
                        D.LogEx(ex, "Failed preparing agent: {0} ({1} times)", agent, count);
                    }
                }
            }
            // we used this when evaluating personal goals
            goalQueue.Clear();

            #if !PRODUCTION_BUILD
            long passStart = Stats.Now();
            #endif
            bool needsUIRefresh = false;
            isColonyGoalsLocked = true;
            AIGoal curG = null;
            bool canBid = auction.Agents.Count > 0;
            auctionIndex.Clear(JobType.Count);
            admitChanged = false;
            try {
                isAnyFlammableOnFire = S.Sys.Firex.FlammablesOnFire > 0;
                foreach (var g in colonyGoals) {
                    #if !PRODUCTION_BUILD
                    if (g.Value.State == AIGoalState.Bidding) {
                        Stats.BiddingGoals++;
                    }
                    #endif
                    var prio = g.Value.GoalPriority;
                    if (prio < goalsByPriority.Length) {
                        goalsByPriority[prio].Add(g.Value);
                    }
                }
                for (int i = 5; i >= 0; i--) {
                    var goalsOfPrio = goalsByPriority[i];
                    for (int j = 0; j < goalsOfPrio.Count; j++) {
                        var goal = goalsOfPrio[j];
                        curG = goal;
                        // per goal, so one broken goal does not starve the rest
                        try {
                            needsUIRefresh |= ProcessColonyGoal(goal, ticks, canBid);
                        } catch (Exception ex) {
                            if (errorThrottle.ShouldLog(curG, out var count)) {
                                D.LogEx(ex, "Failed to process colony goal: {0} ({1} times)",
                                    curG, count);
                            }
                            // back off like the auction does, or the goal throws again
                            // on every tick
                            try {
                                curG.MarkFailed();
                                AIGoal.Fail(curG, ticks, T.AdRejUnreachable);
                                AIGoal.SetRetryBackoff(curG, ticks);
                            } catch (Exception fex) {
                                if (errorThrottle.ShouldLog(curG, out _)) {
                                    D.LogEx(fex, "Failed to fail colony goal: {0}", curG);
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to process colony goal: {0}", curG);
            }
            isColonyGoalsLocked = false;
            for (int i = 0; i < goalsByPriority.Length; i++) {
                goalsByPriority[i].Clear();
            }
            auctionIndex.Build();

            #if !PRODUCTION_BUILD
            Stats.PassMs = Stats.MsSince(passStart);
            Stats.IdleAgents = auction.Agents.Count;
            #endif
            if (auction.Agents.Count > 0 && auctionIndex.Count > 0) {
                // Prioritize current goals that have no agent assigned
                // AIAuction fails a throwing goal by itself; this is for whatever is left,
                // so the agents below still run this tick
                try {
                    #if !PRODUCTION_BUILD
                    Stats.BeginAuction();
                    #endif
                    using (D.Timer("Auction.Run", 0.0001f)) {
                        auction.Run(S, ticks, auctionIndex);
                    }
                    #if !PRODUCTION_BUILD
                    Stats.EndAuction();
                    #endif
                    needsUIRefresh |= admitChanged;
                } catch (Exception ex) {
                    if (errorThrottle.ShouldLog(auction, out var count)) {
                        D.LogEx(ex, "AI auction failed ({0} times)", count);
                    }
                }
                foreach (var goal in auction.GoalsWithBids) {
                    try {
                        // A goal over the re-check budget went to the auction unchecked.
                        // One check per assignment keeps the winner from committing to a
                        // stale goal; it idles this tick and bids again on the next.
                        if (!IsGateStale(goal, ticks) || RecheckGate(goal, ticks)) {
                            #if !PRODUCTION_BUILD
                            var bidAgent = goal.BestBid.Agent;
                            var bidFrom = bidAgent.PosIdx;
                            if (goal.BestBid.Assign(ticks)) {
                                Stats.Assignments++;
                                var reach = goal.ReachEntity;
                                if (reach != null) {
                                    Stats.AssignDistanceSum += Pos.Distance(bidFrom, reach.PosIdx);
                                    MeasureAssignHops(bidAgent, bidFrom, goal, reach);
                                }
                            }
                            #else
                            goal.BestBid.Assign(ticks);
                            #endif
                        }
                    } catch (Exception ex) {
                        if (errorThrottle.ShouldLog(goal, out var count)) {
                            D.LogEx(ex, "Failed assigning goal bid: {0} ({1} times)", goal, count);
                        }
                    }
                    goal.BestBid = default;
                    needsUIRefresh = true;
                }
            }
            #if !PRODUCTION_BUILD
            if (Stats.BiddingGoals > 0) {
                Stats.IdleUnassigned = auction.Agents.Count;
            }
            #endif
            // goals every capable worker was turned away from get a verdict instead of
            // waiting silently; bid winners are already assigned and get skipped
            try {
                unreachable.Resolve(auction.WalkRejects, agents, S.Sys.Areas, ticks);
            } catch (Exception ex) {
                if (errorThrottle.ShouldLog(unreachable, out var count)) {
                    D.LogEx(ex, "Failed resolving unreachable tasks ({0} times)", count);
                }
            }

            #if !PRODUCTION_BUILD
            long agentsStart = Stats.Now();
            #endif
            // Per agent, so one agent that throws does not stop every agent after it
            for (int i = agents.Count - 1; i >= 0; i--) {
                if (i >= agents.Count) { continue; }
                AIAgentComp curAgent = agents[i];
                try {
                    if (curAgent.Being.IsAlive) {
                        needsUIRefresh |= RunAgent(curAgent, ticks);
                    } else {
                        // D.Warn("removing dead agent: {0}", curAgent);
                        curAgent.RemoveAgent();
                    }
                } catch (System.Exception ex) {
                    if (curAgent == null) {
                        D.LogEx(ex, "Unexpected error in agent loop, no agent!");
                    } else {
                        // an agent that is broken for good throws on every tick
                        if (errorThrottle.ShouldLog(curAgent, out var count)) {
                            D.LogEx(ex, "Agent failed running: {0}. Act: {1}, Goal: {2} ({3} times)",
                                curAgent, curAgent.CurrentAction, curAgent.Goal, count);
                            ModRegistry.WarnAboutBrokenMod(curAgent.CurrentAction,
                                $"Exception while executing AIAction: {curAgent.CurrentAction}");
                        }
                        if (curAgent.Goal != null) {
                            RegisterError(curAgent.Goal);
                        }
                        // a second throw here used to escape OnTick and abort the tick
                        try {
                            curAgent.ClearGoal();
                        } catch (Exception cex) {
                            D.LogEx(cex, "Failed clearing goal after agent error: {0}", curAgent);
                        }
                        needsUIRefresh = true;
                    }
                }
            }

            #if !PRODUCTION_BUILD
            Stats.AgentsMs = Stats.MsSince(agentsStart);
            #endif
            needsUIRefresh |= RemovePendingGoals();
            try {
                unreachable.Reconcile(colonyGoals);
            } catch (Exception ex) {
                if (errorThrottle.ShouldLog(unreachable, out var count)) {
                    D.LogEx(ex, "Failed reconciling unreachable tasks ({0} times)", count);
                }
            }
            #if !PRODUCTION_BUILD
            Stats.EndTick();
            #endif
        }

        #if !PRODUCTION_BUILD
        // The region hops of the walk to a task just assigned, for AIAuctionPerfTests. Kept
        // out of the measured tick time, since only the measurement needs it
        private void MeasureAssignHops(AIAgentComp agent, int from, AIGoal goal, Entity reach) {
            long start = Stats.Now();
            bool near = (goal.Flags & AIGoalFlags.LocationExact) == AIGoalFlags.None;
            int w = 0;
            int h = 0;
            if (near && reach is Tile tile && tile.Transform.IsMulti) {
                w = tile.Transform.RotatedWidth;
                h = tile.Transform.RotatedHeight;
            }
            var req = Game.Systems.Reg.RegPathReq.Create(from, reach.PosIdx,
                agent.Motion.IsGrounded, near, w, h);
            req.Permissions = agent.Motion.Permissions;
            int hops = S.Query.RegionDist.Ask(req);
            if (hops >= 0) {
                Stats.AssignHopsSum += hops;
                Stats.AssignHopsCount++;
            }
            Stats.MeasureMs += Stats.MsSince(start);
        }
        #endif

        // Building floor to a task cut off from its workers is the usual fix, so the task
        // retries at once instead of waiting out its backoff
        private void OnIslandCountChanged(int before, int now) {
            unreachable?.RetryNow(G.Ticks);
        }

        private static void CheckPersonalGoal(long ticks, AIAgentComp agent) {
            if (agent.TopPersonalGoal == null) { return; }
            if (!agent.TopPersonalGoal.ShouldInterrupt(agent.Goal, ticks)) {
                return;
            }
            if (!agent.TopPersonalGoal.BestBid.IsValid) {
                return;
            }
            #if DEBUG_AI_PLANNER
            D.Warn("Reassigning top goal for {0}: {1} -> {2}",
                agent, agent.Goal, agent.TopPersonalGoal);
            #endif
            var g = agent.Goal;
            if (g != null) {
                if (agent.CurrentAction == null) {
                    // let's ensure that we have an action before we try to
                    // interrupt the goal, or the agent BB may not be cleared in mid execution
                    EnsureCurrentAction(agent, ticks, g);
                }
                // reason before the state change: the UI rebuild and the target
                // are notified during it
                g.FailReason = $"{T.AdRejInterrupted}: {agent.TopPersonalGoal.Name}";
                g.ChangeStateTo(AIGoalState.Interrupted, ticks);
            }
            agent.ClearGoal();
            agent.TopPersonalGoal.BestBid.Assign(ticks);
        }

        private bool RemovePendingGoals() {
            if (goalsToRemove.Count > 0) {
                if (isColonyGoalsLocked) {
                    D.Err("Trying to remove pending goal while colony goals are locked!");
                    isColonyGoalsLocked = false;
                }
                for (int i = goalsToRemove.Count - 1; i >= 0; i--) {
                    uint id = goalsToRemove[i];
                    if (colonyGoals.TryGetValue(id, out var goal)) {
                        colonyGoals.Remove(id);
                        RemoveConstructionGoal(goal);
                    } else {
                        #if DEBUG_AI_PLANNER
                        D.Err("Failed removing colony goal by id: {0} => {1}",
                            id, D.Dump(colonyGoals));
                        #endif
                    }
                }
                goalsToRemove.Clear();
                return true;
            }
            return false;
        }

        private void RemoveConstructionGoal(AIGoal goal) {
            if (constructionGoals.Remove(goal)) {
                S.Situation.OngoingBuildTasks = constructionGoals.Count;
            }
        }

        private void FindTopPersonalGoalFor(bool isUnderDC, AIAgentComp agent, long ticks) {
            bool isCombatIdle = agent.CurrentAction == null && !agent.CombatUnit.CanIdle;
            // Idle agents skip the throttle entirely: they have no task to break
            // away from, so we always want a fresh read of their personal goals.
            bool bypassThrottle = isCombatIdle || agent.IsIdle;
            if (!bypassThrottle && agent.NextPersonalGoalCheckTick > ticks) {
                agent.TopPersonalGoal = null;
                return;
            }
            if (!bypassThrottle) {
                agent.NextPersonalGoalCheckTick = ticks + Consts.TicksPer5Minutes;
            }
            goalQueue.Clear();
            if (!isUnderDC) {
                EnqueuePrivateGoals(agent, agent.Goals, ticks);
            }
            // Combat unit goals will only be considered if unit is aware of combat
            // A retreating colonist (CombatUnit.IsRetreating) leaves the fight to its needs
            if (isUnderDC || (agent.CombatUnit.State != CombatUnitState.Unaware
                    && !agent.CombatUnit.IsRetreating)) {
                if (agent.CombatUnit.Goals.Count > 0) {
                    EnqueuePrivateGoals(agent, agent.CombatUnit.Goals, ticks);
                }
            #if !PRODUCTION_BUILD
            } else {
                if (agent.Goal?.Type == AIGoalType.Combat && !agent.CombatUnit.IsAttackTargetForced) {
                    D.Err("Unaware combat unit has a combat goal active! {0}",
                        agent.Goal);
                }
            #endif
            }
            if (goalQueue.Count > 0) {
                agent.TopPersonalGoal = goalQueue.Dequeue();
            } else {
                agent.TopPersonalGoal = null;
            }
        }

        private void EnqueuePrivateGoals(AIAgentComp agent, List<AIGoal> goals,
                long ticks) {
            for (int i = goals.Count - 1; i >= 0; i--) {
                AIGoal g = goals[i];
                // needs / combat objective retriggers them. We'll skip those
                if (!AIGoal.IsActive(g.State)) {
                    continue;
                }
                if (g.CanRetryAfterTick > ticks) {
                    continue;
                }
                if (agent.BB.IsGoalCoolingDown(g.Id, ticks)) {
                    continue;
                }

                if (planner.TryFormulateFor(g, agent, ticks, out var plan)) {
                    g.BestBid = AIBid.For(ticks, g, agent, plan, 0);
                    if (g.BestBid.IsValid) {
                        if (g.PriorityResolver?.UpdatePriorityFor(g, agent, ticks) != false) {
                            if (g.CheckDoable?.Invoke(S, g).IsDoable != false) {
                                goalQueue.Enqueue(g, g.UpdateInternalPriority(ticks)
                                    - g.PriorityTieBreaker);
                            }
                        }
                    }
                }
            }
        }

        private void SetRetryFor(AIGoal goal, long ticks, int retryDelay = 0) {
            AIGoal.SetRetryBackoff(goal, ticks, retryDelay);
            #if DEBUG_AI_PLANNER
            D.Warn("Setting retry delay: {0} / {1}",
                Units.TicksToTime(goal.CanRetryAfterTick - ticks), goal);
            #endif
        }

        private void SetRetryFor(AIGoal goal, long ticks,
                in AIGoalCheckResult checkRes) {
            if (checkRes.IsRetryable) {
                goal.Flags &= ~AIGoalFlags.NonRetryable;
                SetRetryFor(goal, ticks, checkRes.RetryAfterTicks);
            } else {
                goal.Flags |= AIGoalFlags.NonRetryable;
                goal.CanRetryAfterTick = 0L;
            }
        }

        private bool CheckGoalTarget(AIGoal goal) {
            if (goal.Target == null) {
                return true;
            }
            if (!goal.Target.Entity.IsActive) {
                // D.ErrSoft("Sanity check! Goal target entity is inactive! {0} -> {1} -> {2}",
                //     goal, goal.Target, goal.Target.Entity.Goal);
                AIGoal.Cancel(goal, G.Ticks, removeImmediately: false,
                    reason: T.AdRejTargetUnavailable);
                return false;
            }
            if (goal.Target.Entity.Goal != goal) {
                // A target that let go of the goal on purpose (UnstoredMatComp drops a failed
                // non-retryable haul goal, which a later check here may have made retryable
                // again) leaves it orphaned: it only has to go. Another goal on the target is
                // the anomaly worth reporting.
                if (goal.Target.Entity.Goal != null) {
                    D.Err("Sanity check! Goal target entity doesn't have the same goal assigned! {0} -> {1} -> {2}",
                        goal, goal.Target, goal.Target.Entity.Goal);
                }
                AIGoal.Cancel(goal, G.Ticks, removeImmediately: false);
                return false;
            }
            return true;
        }

        /// <returns>true if goal changed</returns>
        private bool ProcessColonyGoal(AIGoal goal, long ticks, bool canBid) {

            if (isAnyFlammableOnFire && AIGoal.IsBlockedByFire(goal)) {
                switch (goal.State) {
                    case AIGoalState.InProgress: {
                        // the target caught fire mid-task; stop working on it
                        // until the fire is out (Interrupted recovers later)
                        goal.FailReason = T.CondOnfire;
                        goal.ChangeStateTo(AIGoalState.Interrupted, ticks, true);
                        return true;
                    }
                    case AIGoalState.Pending:
                    case AIGoalState.Interrupted:
                    case AIGoalState.Available:
                    case AIGoalState.Bidding:
                    case AIGoalState.PreAssigned: {
                        // silently wait until the fire is out, no state churn
                        return false;
                    }
                }
                // other states (Failed, Cancelled, etc) fall through to the
                // regular processing below
            }

            switch (goal.State) {
                case AIGoalState.Interrupted:
                case AIGoalState.Pending: {
                    if (!CheckGoalTarget(goal)) {
                        return true;
                    }
                    if (goal.IsNanobotWorkable) { return false; }
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                    return true;
                }
                case AIGoalState.Available: {
                    if (!CheckGoalTarget(goal)) {
                        return true;
                    }
                    return ProcessGoalAvailable(goal, ticks);
                }
                case AIGoalState.PreAssigned: {
                    if (!CheckGoalTarget(goal)) {
                        return true;
                    }
                    return ProcessGoalPreAssigned(goal, ticks, false);
                }
                case AIGoalState.Bidding: {
                    if (goal.IsNanobotWorkable) { return false; }
                    if (goal.GoalPriority == 0) { return false; }
                    if (!CheckGoalTarget(goal)) {
                        return true;
                    }
                    if (canBid) {
                        // A stale gate would hand a worker a goal it fails at once (STA-1631:
                        // storing goals kept going out after every storage had filled up).
                        // Checked here, not when a worker looks at the goal, so a goal no idle
                        // worker can do still shows why it waits. Over budget, the goal goes to
                        // the auction unchecked as before, so this never holds work back.
                        if (GateRechecksThisTick < MaxGateRechecksPerTick
                                && IsGateStale(goal, ticks)) {
                            GateRechecksThisTick++;
                            if (!RecheckGate(goal, ticks)) {
                                return true;
                            }
                        }
                        // the auction ranks goals by AIAuctionIndex.RankFor, so a colony goal
                        // needs no CurrentPriority
                        var job = goal.Job;
                        if (job != null) {
                            auctionIndex.Add(goal, job.Index, ticks);
                        }
                    }
                    return false;
                }
                case AIGoalState.Cancelled: {
                    RemoveGoal(goal);
                    return false;
                }
                case AIGoalState.Failed: {
                    return ProcessGoalFailed(goal, ticks);
                }
                case AIGoalState.Error: {
                    RemoveGoal(goal);
                    RegisterError(goal);
                    return true;
                }
                case AIGoalState.Completed: {
                    #if !PRODUCTION_BUILD
                    Stats.GoalsCompleted++;
                    #endif
                    RemoveGoal(goal);
                    return false;
                }
                case AIGoalState.InProgress: {
                    if (goal.Agent == null) {
                        goal.FailReason = "Goal in progress, but no agent attached";
                        goal.ChangeStateTo(AIGoalState.Error, ticks);
                        return true;
                    } else if (goal.Agent.Goal != goal) {
                        if (!CheckGoalTarget(goal)) {
                            return true;
                        }
                        // agent is not doing the goal that the goal is expecting!
                        goal.FailReason = "Goal in progress, but agent is not doing it";
                        goal.ChangeStateTo(AIGoalState.Failed, ticks);
                        return true;
                    }
                    return false;
                }
                default: {
                    RemoveGoal(goal);
                    RegisterError(goal);
                    D.Err("Removing a goal with unhandled state: {0} -> {1}",
                        goal.State, goal);
                    return false;
                }
            }
        }

        private void RemoveGoal(AIGoal goal, bool immediately = false) {
            if (goal.Type != AIGoalType.Colony) { return; }
            if (goal.Id == 0) {
                D.Err("Trying to enqueue goal removal with 0 id! {0}", goal);
            }
            goalsToRemove.Add(goal.Id);
        }

        private void RegisterError(AIGoal goal) {
            if (goal == null) {
                D.Err("Trying to register null goal as error!");
                return;
            }
            goalsWithError ??= new();
            // A goal whose action throws is retried and lands here again on every retry,
            // so the list grew for the whole session, keeping every target alive and
            // going into every save. One entry per goal, and only the latest ones.
            if (goalsWithError.Contains(goal)) { return; }
            if (goalsWithError.Count >= MaxGoalsWithError) {
                goalsWithError.RemoveAt(0);
            }
            goalsWithError.Add(goal);
        }
        private const int MaxGoalsWithError = 100;

        private bool ProcessGoalPreAssigned(AIGoal goal, long ticks, bool showFailTooltip) {
            if (goal == null) {
                D.Err("Trying to preassign a null goal!");
                return true;
            }
            if (AIGoal.IsBlockedByFire(goal)) {
                if (showFailTooltip) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(T.CondOnfire), true);
                }
                return false;
            }
            if (goal.Agent == null || goal.Agent.Being == null
                    || !goal.Agent.Being.IsActive || goal.Agent.Being.IsDead) {
                D.Err("PreAssigned goal has null/inactive/dead agent! {0} | {1}",
                    goal, goal.Agent?.Being);
                if (goal.NonRetryable) {
                    goal.ChangeStateTo(AIGoalState.Failed, ticks);
                } else {
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                }
                return true;
            }
            // we have an agent pre-assigned, let's try to make a bid
            var agent = goal.Agent;

            if (!goal.IsAvailabilityGateOpen(S)) {
                return false;
            }

            if (agent.Jobs == null) {
                D.Err("Agent mind.jobs is null, cannot pre-assign goal! {0} / {1}", agent, goal);
                return true;
            }

            if (!goal.IsAllowedFor(agent)) {
                D.Err("Pre-assigned goal was not allowed for the worker: {0} => {1}",
                    goal, agent);
                goal.FailReason = T.OperatableWorkerNotAllowed;
                if (showFailTooltip) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(goal.FailReason), true);
                }
                if (goal.IsForceAssigned) {
                    goal.ChangeStateTo(AIGoalState.Failed, ticks);
                } else {
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                }
                goal.SetAgent(null);
                return true;
            }

            if (!BeingUtils.HasAbilities(agent.BeingData, goal.RequiredAbilities)) {
                D.Warn("Agent incapable of doing preassigned goal: {0}->{1}", goal, agent);
                if (showFailTooltip) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(T.AdjIncapable), true);
                }
                goal.SetAgent(null);
                if (goal.NonRetryable) {
                    goal.ChangeStateTo(AIGoalState.Failed, ticks);
                } else {
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                }
                return false;
            }

            // check if agent has job priority for this
            if (goal.Type == AIGoalType.Colony) {
                int jobPrio = -1;
                if (agent.Jobs.TryJob(goal.JobIdH, out var jobSlot)) {
                    jobPrio = jobSlot.Priority;
                }
                if (jobPrio < 1) {
                    goal.SetAgent(null);
                    string reason = jobPrio == 0 ?  T.BrainStatJobpriority0
                        : T.GoalWorkerIncapable;
                    goal.FailReason = reason;
                    if (showFailTooltip) {
                        The.SysSig.ShowTooltip.Send(Texts.Red(reason), true);
                    }
                    if (goal.IsForceAssigned) {
                        // Refused, as a work type the worker is not assigned to should be:
                        // this marked the goal failed with the reason shown, then fell
                        // through and assigned it anyway
                        goal.ChangeStateTo(AIGoalState.Failed, ticks);
                        return true;
                    } else {
                        goal.ChangeStateTo(AIGoalState.Available, ticks);
                        return true;
                    }
                }
            }

            // If goal equals to agent goal, it is a replan request
            var ag = agent.Goal;
            if (ag != null && ag != goal) {
                if (ag.IsNonInterruptable) {
                    bool canInterrupt = false;
                    if (goal.IsNonInterruptable) {
                        if (goal.InterruptPriority > ag.InterruptPriority) {
                            canInterrupt = true;
                        }
                    }
                    if (!canInterrupt) {
                        D.Warn("Trying to pre-assign a goal while agent is working on a non-interruptable: {0} -> {1}",
                            ag, goal);
                        if (showFailTooltip) {
                            The.SysSig.ShowTooltip.Send(Texts.Red(T.CannotInterrupt), true);
                        }
                        return false;
                    }
                }
                if (goal.IsForceAssigned) {
                    ag.FailReason = T.GoalStateInteruptedDirectOrder_V1.F(
                        goal.Name);
                    if (agent.CurrentAction == null) {
                        // The old goal may be between actions, with handoff state
                        // waiting for the next action to clean it up.
                        EnsureCurrentAction(agent, ticks, ag);
                    }
                    ag.ChangeStateTo(AIGoalState.Failed, ticks);
                    agent.ClearGoal();
                } else {
                    // non-force assigned goal is being set!
                    if (goal.IsStreamlined && agent.Goal.IsStreamlined) {
                        // both goals are streamlined, let's wait for the current
                        // agent goal to be finished
                        return false;
                    }
                    // agent is busy, this was not force assigned, so we can
                    // transition this goal to Available
                    D.Warn("Agent busy with {0}, while trying to get the pre-assigned goal {1}. {2}",
                        ag, goal, agent);
                    goal.SetAgent(null);
                    goal.FailReason = T.GoalAssignedWorkerBusy_V1.F(ag.Name);
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                    return true;
                }
            }
            if (planner.TryFormulateFor(goal, agent, ticks, out var plan)) {
                // if we want to show fail tooltip, it means we are forcing this from UI
                agent.SetGoal(goal, plan, ticks, force: showFailTooltip);
                auction.RemoveAgent(agent);
                return true;
            } else {
                goal.SetAgent(null);
                goal.FailReason = T.GoalWorkerIncapable;
                if (showFailTooltip) {
                    The.SysSig.ShowTooltip.Send(Texts.Red(T.AdjIncapable), true);
                }
                if (goal.IsForceAssigned) {
                    goal.ChangeStateTo(AIGoalState.Failed, ticks);
                } else {
                    // agent is unable to perform the goal
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                }
                return true;
            }
        }

        private bool ProcessGoalAvailable(AIGoal goal, long ticks) {
            if (goal.IsNanobotWorkable) {
                return false;
            }
            if (!CheckWorkerAbilities(goal, ticks)) {
                return true;
            }
            if (!goal.IsAvailabilityGateOpen(S)) {
                return false;
            }
            if (goal.CheckDoable != null) {
                var checkRes = goal.CheckDoable(S, goal);
                if (!checkRes.IsDoable) {
                    ApplyFailedCheck(goal, in checkRes, ticks);
                    return true;
                }
            }
            goal.GateCheckTick = ticks;
            // -> Bidding
            goal.ChangeStateTo(AIGoalState.Bidding, ticks);
            return true;
        }

        private static bool IsGateStale(AIGoal goal, long ticks) {
            return goal.CheckDoable != null && ticks - goal.GateCheckTick >= GateRecheckTicks;
        }

        /// <returns>false when the gate failed and the goal left Bidding</returns>
        private bool RecheckGate(AIGoal goal, long ticks) {
            goal.GateCheckTick = ticks;
            var checkRes = goal.CheckDoable(S, goal);
            if (checkRes.IsDoable) { return true; }
            ApplyFailedCheck(goal, in checkRes, ticks);
            return false;
        }

        private void ApplyFailedCheck(AIGoal goal, in AIGoalCheckResult checkRes, long ticks) {
            if (checkRes.RetryAfterTicks > 0) {
                // an explicit cadence means the gate re-checks cheaply: this is
                // a wait, not a defect, so it must not escalate the backoff of
                // the next real failure
                goal.TimesFailedTotal++;
            } else {
                goal.MarkFailed();
            }
            goal.FailReason = checkRes.Reason;
            if (checkRes.Icon != null) {
                goal.ChangeStateTo(checkRes.WantedState, ticks, true,
                    failIcon: checkRes.Icon);
            } else {
                goal.ChangeStateTo(checkRes.WantedState, ticks, true);
            }
            if (checkRes.WantedState == AIGoalState.Failed) {
                SetRetryFor(goal, ticks, in checkRes);
            }
        }

        private bool CheckWorkerAbilities(AIGoal goal, long ticks) {
            if (!CheckWorkerAbilities(goal, goal.Job, ticks)) {
                return false;
            }
            return true;
        }

        private bool CheckWorkerAbilities(AIGoal goal, JobType job, long ticks) {
            if (!colonyJobTypes.Contains(job.IdHash)) {
                goal.MarkFailed();
                goal.FailReason = T.JobFailNoWorker_V1.F(job.NameT);
                goal.ChangeStateTo(AIGoalState.Failed, ticks,
                    failIcon: IconId.CLackSkill);
                SetRetryFor(goal, ticks);
                return false;
            }
            return true;
        }

        // The auction lets a bidding goal in when a worker first looks at it this tick, so
        // these checks run for the inspected goals only, not for every bidding goal (the
        // stale doability gate is re-checked earlier, in the bidding pass).
        // Returns false when the goal is not biddable now (it may have left Bidding).
        private bool AdmitGoal(AIGoal goal, long ticks) {
            if (!goal.IsAvailabilityGateOpen(S)) {
                return false;
            }
            // last minute check - can we reach the evaluation position?
            // Uses goal.ReachEntity which may differ from goal.Target (e.g. Reinstall
            // evaluates reachability at the source tile, not the destination).
            var pos = goal.ReachEntity?.PosIdx ?? 0;
            if (pos > 0 && !S.Query.IsRegionReachable.Ask(pos)) {
                // For multi-tile objects, the root position may be
                // unreachable while another subtile position is reachable
                // (e.g. when a large tile spans chunk boundaries).
                bool isReachable = false;
                if (goal.ReachEntity is Tile tile && tile.Transform.IsMulti) {
                    var posCache = Caches.IntArray128;
                    int cnt = tile.Transform.GetSubtilePos(posCache);
                    for (int i = 0; i < cnt; i++) {
                        if (S.Query.IsRegionReachable.Ask(posCache[i])) {
                            isReachable = true;
                            break;
                        }
                    }
                }
                if (!isReachable) {
                    // No passable tile next to it, so nobody at all can get there
                    AIGoal.Fail(goal, G.Ticks,
                        $"{T.AdRejUnreachableWalledIn} {Texts.LinkTo(Pos.String(pos), pos)}");
                    SetRetryFor(goal, G.Ticks);
                    admitChanged = true;
                    return false;
                }
            }
            return true;
        }

        private bool ProcessGoalFailed(AIGoal goal, long ticks) {
            if (goal.NonRetryable) {
                // remove non-retryable failed goal
                RemoveGoal(goal);
            } else {
                if (!CheckGoalTarget(goal)) {
                    return true;
                }
                if (ticks > goal.CanRetryAfterTick) {
                    // we can put this back to Available
                    goal.ChangeStateTo(AIGoalState.Available, ticks);
                    // we had the icon show failure
                    goal.UpdateIcon();
                    return true;
                }
                // else we keep it here
            }
            return false;
        }

        /// <returns>true if UI needs refresh (something changed)</returns>
        private bool RunAgent(AIAgentComp agent, long ticks) {
            var goal = agent.Goal;
            var skipRun = agent.Motion.SkipRunAgent;
            if (goal == null && skipRun) { return false; }
            if (agent.BeingData.SkipSimulate) {
                D.Err("Trying to run an inactive agent: {0}", agent);
                return false;
            }

            if (goal == null && !skipRun) {
                if (agent.DirectControl.IsUnderDirectControl) {
                    // don't idle or do personal goals for folks under direct control
                    return false;
                }
                var topG = agent.TopPersonalGoal;
                if (topG != null) {
                    if (AIGoal.IsAvailable(topG.State)) {
                        if (agent.TopPersonalGoal.BestBid.IsValid) {
                            agent.TopPersonalGoal.BestBid.Assign(ticks);
                            return true;
                        }
                    } else {
                        agent.TopPersonalGoal = null;
                    }
                }
                if (agent.Dockable?.IsDocked == true) {
                    // let's not do anything to docked agents
                    return false;
                }
                if (agent.Motion.HasProgressFlag(Motion.MotionProgressFlags.Teleporting)) {
                    // Allow the teleportation to end
                    return false;
                }
                // Idle non-docked agents
                goal = AIGoal.Idle(agent, ticks);
                if (planner.TryFormulateFor(goal, agent, ticks, out var idlePlan)) {
                    // D.Warn("Idle plan: {0}", idlePlan);
                    agent.SetGoal(goal, idlePlan, ticks);
                } else {
                    // D.Err("Failed to idle for agent: {0}, {1}", agent, goal);
                    goal.FailReason = "Failed to formulate idle plan!";
                    goal.ChangeStateTo(AIGoalState.Error, ticks);
                    return false;
                }
            }

            if (goal.State != AIGoalState.InProgress) {
                if (AIGoal.IsAvailable(goal)) {
                    D.ErrSoft("Agent trying to run a goal that is not in progress! {0} | {1} | {2}",
                        agent, goal, goal.Target?.Entity);
                    goal.DumpStateTrace();
                    goal.ChangeStateTo(AIGoalState.InProgress, ticks);
                } else {
                    agent.ClearGoal();
                }
                return true;
            }

            AIAction lastAction = EnsureCurrentAction(agent, ticks, goal);
            var act = agent.CurrentAction;
            if (act == null) {
                // We are done
                if (goal.CheckDone(ticks)) {
                    RemoveGoal(goal);
                } else {
                    D.Err("It is possible that action {0} did not set the world state of the goal! {1}. Plan: {2}",
                        lastAction?.Id, goal, D.DumpInstance(agent.Plan));
                    D.Err("Agent ran out of actions but goal is not done! {0} -> {1}. Plan: {2}. Last agent action: {3}",
                        agent, goal, agent.Plan, lastAction);
                    goal.FailReason = "Agent executed the plan, but goal not done";
                    goal.ChangeStateTo(AIGoalState.Failed, ticks);
                    goal.ResetWorldState();
                    #if DEBUG_AI_PLANNER
                    D.Err("TODO handle goal failed: {0}", goal);
                    #endif
                }
                agent.ClearGoal();
                return true;
            }

            if (agent.ActionsReloaded) {
                agent.ActionsReloaded = false;
                // A part burnt or shot off mid-action can take the abilities the action
                // needs: hand the goal back instead of working without them
                if (!agent.Actions.Contains(act)) {
                    if (!AIGoal.Interrupt(goal, ticks, T.GoalWorkerIncapable)) {
                        AIGoal.Fail(goal, ticks, T.GoalWorkerIncapable);
                    }
                    agent.ClearGoal();
                    return true;
                }
            }

            var result = act.Execute(S, agent, goal, ticks);

            goal.LastResult = result;
            switch (result.State) {
                case AIActionState.InProgress: {
                        agent.TicksInCurrentAction++;
                        BeingUtils.IncrementCounterFor(agent.BeingData, act);
                        return false;
                    }
                case AIActionState.NeedsReplan: {
                        goal.ChangeStateTo(AIGoalState.Pending, ticks);
                        agent.ClearGoal();
                        if (goal.TrySetAgent(agent)) {
                            goal.ChangeStateTo(AIGoalState.PreAssigned, ticks);
                        } else {
                            goal.FailReason = T.GoalWorkerIncapable;
                            goal.ChangeStateTo(AIGoalState.Failed, ticks);
                        }
                        return true;
                    }
                case AIActionState.Success: {
                        // There's a chance that ai agent is gone after this, for example
                        // entered a shuttle. In that case agent is basically removed
                        // and we have less work to do
                        if (agent.BeingData.SkipSimulate) {
                            if (goal.CheckDone(ticks)) {
                                RemoveGoal(goal);
                            }
                            return true;
                        }
                        if (agent.CurrentAction != null) {
                            agent.CurrentAction.OnDeactivate(agent, goal, ticks);
                            agent.SetAction(null);
                        } else {
                            #if DEBUG_AI_PLANNER
                            // it's possible to get it at high speed when performing
                            // demolition tasks
                            D.Err("Agent's current action is null! {0}: {1}",
                                agent, goal);
                            #endif
                        }
                        if (goal.CheckDone(ticks)) {
                            RemoveGoal(goal);
                            agent.ClearGoal();
                        }
                        return true;
                    }
                case AIActionState.FailedSoft: {
                    #if DEBUG_AI_PLANNER
                    D.Err("Action soft-failed: {0} -> {1} | {2}", act, result.Message, goal);
                    #endif
                        goal.FailReason = result.Message;
                        goal.ChangeStateTo(AIGoalState.Available, ticks);
                        agent.ClearGoal();
                        goal.ResetWorldState();
                        goal.UpdateIcon();
                        return true;
                    }
                case AIActionState.Cancelled: {
                    #if DEBUG_AI_PLANNER
                    D.Err("Action cancelled: {0} -> {1}", act, result.Message);
                    #endif
                        AIGoal.Cancel(goal, ticks, result.Message,
                            removeImmediately: false);
                        return true;
                    }
                case AIActionState.PutBack: {
                        // A colony goal was admitted when it went out, so it bids again at
                        // once: through Available it would miss the worker's next auction,
                        // which then gave the worker a lesser goal (STA-1740)
                        goal.ChangeStateTo(goal.Type == AIGoalType.Colony
                            ? AIGoalState.Bidding : AIGoalState.Available, ticks);
                        agent.ClearGoal();
                        goal.ResetWorldState();
                        return true;
                    }
                case AIActionState.Failed: {
                    #if DEBUG_AI_PLANNER
                    D.Err("TODO: handle action failed");
                    D.Err("Action failed: {0} -> {1}", act, result.Message);
                    #endif
                        goal.MarkFailed();
                        goal.FailedAgentId = agent.BeingData.Id;
                        goal.FailReason = result.Message;
                        var fIcon = result.Icon;
                        if (fIcon == null) {
                            goal.ChangeStateTo(AIGoalState.Failed, ticks);
                        } else {
                            if (fIcon == string.Empty) {
                                fIcon = null;
                            }
                            goal.ChangeStateTo(AIGoalState.Failed, ticks,
                                failIcon: fIcon);
                        }
                        SetRetryFor(goal, ticks);
                        // agent.CurrentAction.OnDeactivate(agent, goal);
                        agent.ClearGoal();
                        return true;
                    }
                case AIActionState.Error: {
                        goal.FailReason = result.Message;
                        goal.ChangeStateTo(AIGoalState.Error, ticks);
                        agent.ClearGoal();
                        return true;
                    }
                default: {
                    #if DEBUG_AI_PLANNER
                    D.Err("Unhandled action state in the plan: {0}: {1}", result, act);
                    #endif
                        goal.FailReason = "Unhandled action state in goal plan";
                        goal.ChangeStateTo(AIGoalState.Error, ticks);
                        agent.ClearGoal();
                        return true;
                    }
            }
        }

        public static AIAction EnsureCurrentAction(AIAgentComp agent, long ticks, AIGoal goal) {
            var lastAction = agent.CurrentAction;
            #if DEBUG_COMBAT
            agent.Being.Name.Debug($"{goal?.Name}|{lastAction?.Id}|{agent.TicksInCurrentAction}{CombatPriorities(agent)}");
            #endif
            // agent.Being.Name.Debug($"{SquadRoleIdH.ResolveName(agent.CombatUnit.SquadRole)}");
            if (agent.CurrentAction == null) {
                if (agent.Plan?.TryAdvance(out var action) == true) {
                    agent.SetAction(action);
                    agent.TicksInCurrentAction = 0;
                    action.OnActivate(agent, goal, ticks);
                }
            }

            return lastAction;
        }

        private static string CombatPriorities(AIAgentComp agent) {
            if (agent.CombatUnit.Goals.Count == 0) {
                return null;
            }
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat("\nLOS:{0}.Cov:{1}.Slow:{2}",
                agent.CombatUnit.HasLineOfSight,
                agent.CombatUnit.HasGoodCover,
                agent.Motion.CombatSlowdown);

            foreach (var g in agent.CombatUnit.Goals) {
                sb.AppendFormat("\n<size=50%>{0}:{1}|Imp:{2}|Int:{3}</size>", g.Name, g.GoalPriority,
                    g.GoalImportance, g.InterruptPriority);
            }
            return sb.ToString();
        }

        private void OnRemoveGoal(AIGoal goal) {
            #if !PRODUCTION_BUILD
            // a finished task is usually removed by its target, in progress or completed
            if (goal.Type == AIGoalType.Colony && (goal.State == AIGoalState.InProgress
                    || goal.State == AIGoalState.Completed)) {
                Stats.GoalsCompleted++;
            }
            #endif
            if (AIGoal.IsActive(goal.State) || AIGoal.IsFailed(goal.State)) {
                AIGoal.Cancel(goal, G.Ticks, removeImmediately: false);
            }
            if (goal.Type == AIGoalType.Colony) {
                if (isColonyGoalsLocked) {
                    D.ErrSoft("Trying to remove a goal while other goals are processing! {0}", goal);
                    goalsToRemove.Add(goal.Id);
                } else {
                    colonyGoals.Remove(goal.Id);
                    RemoveConstructionGoal(goal);
                }
            } else {
                D.Err("Only colony goals can be removed! Trying to remove: {0}", goal);
            }
        }

        private void OnAddGoal(AIGoal goal) {
            if (goal.GoalPriority > 2 && ((goal.Flags & AIGoalFlags.ByPlayer) == AIGoalFlags.ByPlayer)) {
                goal.BoostedUntil = G.Ticks + Tunables.NewTaskBoostMinutes;
            }
            if (goal.RequiredAbilities.Length == 0) {
                D.Err("Colony Goal does not have required abilities! Call WithRequiredAbilities() when using CreateGoal()! {0}",
                    goal);
            }
            goal.Validate("Add goal");
            if (goal.State != AIGoalState.Pending) {
                if (goal.State == AIGoalState.PreAssigned) {
                    if (goal.Agent == null) {
                        D.Err("Trying to add PreAssigned goal without an Agent!", goal);
                        RegisterError(goal);
                        return;
                    }
                } else {
                    D.Err("Trying to add a goal with incorrect state! It should be Pending or PreAssigned: {0} -> {1}",
                        goal.State, goal);
                    RegisterError(goal);
                    return;
                }
            } else if (goal.Agent != null) {
                D.Err("Trying to add a Pending goal with an Agent! It should be PreAssigned!",
                    goal);
                RegisterError(goal);
                return;
            }
            if (isColonyGoalsLocked) {
                D.Err("Trying to add a colony goal while colony goals are locked! Will retry on next update: {0}", goal);
                S.Clock.OnNextUpdate(() => OnAddGoal(goal));
                return;
            }

            // the player did not order a hidden task, so nobody being able to do it is not
            // worth a popup or a log line
            if (goal.Agent == null && !goal.IsHidden) {
                var canBeDone = false;
                for (int i = agents.Count - 1; i >= 0; i--) {
                    AIAgentComp agent = agents[i];
                    if (agent.Jobs.CanDo(goal.JobIdH)) {
                        canBeDone = true;
                        break;
                    }
                }
                if (!canBeDone) {
                    canBeDone = HasEnabledNanobotWorkerFor(goal);
                }
                if (!canBeDone) {
                    var msg = $"{T.NoWorkerCapable}: {goal.TaskLine}";
                    if (goal.Target?.Entity != null) {
                        msg = $"{msg} ({Texts.LinkToEntity(goal.Target.Entity)})";
                    }
                    if (S.Prefs.GetBool(Pref.ShowNoWorkerCapable, PrefH.ShowNoWorkerCapable, true)) {
                        if (warnedAboutNoWorkerJobTypeIds.Add(goal.JobIdH)) {
                            // the 3 day reminder replaces an unanswered popup instead of stacking on it
                            WPopup.Create(false, IconId.CWarning, T.Warning, msg)
                                .Unique($"NoWorkerCapable{goal.JobIdH}")
                                .WithAction2(T.DoNotShowAgain, () => {
                                    S.Prefs.SetBool(Pref.ShowNoWorkerCapable, false);
                                })
                                .WithAction(T.Ok, null)
                                .Spawn();
                        }
                    }
                    S.Sys.Log.AddLine(msg);
                }
            }

            colonyGoals.Add(goal.Id, goal);
            AddToConstructionGoals(goal);
            goal.UpdateIcon();
            // try assigning a closest idle worker who would be capable of doing the task
            // ui?.RefreshGoals();
            #if DEBUG_AI_PLANNER
            D.Warn("Adding new AI goal: {0}", goal);
            #endif
        }

        private bool HasEnabledNanobotWorkerFor(AIGoal goal) {
            return goal.IsNanobotCompatible
                && goal.Job?.NanobotCompatible == true
                && S.Query.HasWorkingNanobots.Ask()
                && S.Query.IsNanobotsJobEnabled.Ask(goal.JobIdH);
        }

        private void AddToConstructionGoals(AIGoal goal) {
            if (goal.Target is ConstructableComp) {
                constructionGoals.Add(goal);
                S.Situation.OngoingBuildTasks = constructionGoals.Count;
            }
        }

        private void OnAddAgent(AIAgentComp agent) {
            if (agent.Goal != null) {
                D.Err("Adding agent that already has a goal! {0}->{1}", agent.Goal, agent);
            }
            if (agent.CurrentAction != null) {
                D.Err("Adding agent that already has an action! {0}->{1}->", agent.CurrentAction, agent);
            }
            if (!agents.AddUniqueInefficient(agent)) {
                #if DEBUG_AI_PLANNER
                D.Err("Trying to add a duplicate AI agent: {0}", agent);
                #endif
                return;
            }
            if (agent.BB == null) {
                agent.BB = new AIAgentBlackboard();
                agent.BB.SetAgent(agent);
            }
            #if DEBUG_AI_PLANNER
            D.Warn("Adding new AI agent: {0}", agent);
            #endif
        }

        private void OnRemoveAgent(AIAgentComp agent) {
            #if DEBUG_AI_PLANNER
            D.Warn("Removing AI agent: {0}", agent);
            #endif
            if (agent.Goal != null) {
                // Shuttle, stasis and storage take a living being off the map (the same
                // rule Beings.OnDestroy uses). Blaming death there put a false "Dead"
                // task failure into its log (STA-1655); a null reason is not logged.
                var data = agent.BeingData;
                bool leftAlive = data.HasStateFlag(BeingState.Alive)
                    && data.HasStateFlag(BeingState.Hidden);
                AIGoal.Interrupt(agent.Goal, G.Ticks, leftAlive ? null : T.Dead);
            }
            if (!agents.Remove(agent)) {
                #if DEBUG_AI_PLANNER
                D.Err("Could not find agent to remove: {0}", agent);
                #endif
            }
        }

        // On the main thread, before the save hands the state to its worker thread.
        // SaveSpecial used to do this on the worker, mutating colonyGoals and the situation
        // while main-thread code (EveryFrame, UI goal queries) could still read them.
        private void OnBeforeSave(GameState state, bool willExit) {
            RemovePendingGoals();
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.AIPlanner ??= new AIPlannerSysData();
            sd.AIPlanner.LastGoalId = AIGoal.LastId;
            if (sd.AIPlanner.Agents == null) {
                sd.AIPlanner.Agents = new();
            } else {
                sd.AIPlanner.Agents.Clear();
            }
            if (sd.AIPlanner.Goals == null) {
                sd.AIPlanner.Goals = new();
            } else {
                sd.AIPlanner.Goals.Clear();
            }
            foreach (var g in colonyGoals) {
                // derived from the world by their owner, which rebuilds them after a load
                if (g.Value.IsHidden) { continue; }
                if (AIGoalData.TrySerialize(g.Value, out var data)) {
                    sd.AIPlanner.Goals.Add(data);
                }
            }
            foreach (var a in agents) {
                if (AIAgentData.Serialize(a, out var data)) {
                    sd.AIPlanner.Agents.Add(data.AgentId, data);
                }
            }
            if (!Arrays.IsEmpty(goalsWithError)) {
                sd.AIPlanner.GoalsWithError ??= new();
                try {
                    foreach (var g in goalsWithError) {
                        try {
                            if (AIGoalData.TrySerialize(g, out var data)) {
                                sd.AIPlanner.GoalsWithError.Add(data);
                            }
                        } catch (System.Exception ex) {
                            D.LogEx(ex, "Failed serializing goal with error: {0}", g);
                        }
                    }

                } catch (System.Exception ex2) {
                    D.LogEx(ex2, "Failed to log an error about serializing a goal with error.");
                }
            }
        }

        private AIPlannerSysData data;

        public void LoadSpecial(SystemsDataSpecial sd) {
            colonyGoals.Clear();
            data = sd?.AIPlanner;
            AIGoal.LastId = sd?.AIPlanner?.LastGoalId ?? 0;
        }
    }
}
