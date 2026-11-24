#if UNITY_EDITOR
// #define DEBUG_AI_GOALS
// #define TRACE_AI_GOALS
// #define DEBUG_STATE_CHANGES
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using Priority_Queue;
using System.Diagnostics;
using KL.Collections;
using Game.Systems.UIUX;

namespace Game.Systems.AI {
    public sealed class AIGoal : StablePriorityQueueNode {
        public delegate AIGoalCheckResult AIGoalCheck(GameState S, AIGoal goal);
        public IAIGoalProvider Target;
        /// <summary>
        /// The entity against which REACHABILITY is evaluated in the bidding phase and auction.
        /// Defaults to <see cref="Target"/>.<c>Entity</c> when null.
        /// Transient — not serialized. Providers that need a non-default value must restore
        /// it in <see cref="IAIGoalProvider.OnGoalLoad"/>.
        /// </summary>
        private Entity reachEntity;
        public Entity ReachEntity {
            get => reachEntity ?? Target?.Entity;
            set => reachEntity = value;
        }
        public AIGoal WithReachEntity(Entity e) { reachEntity = e; return this; }
        public AIAgentComp Agent { get; private set; }
        #if TRACE_AI_GOALS
        public string Trace;
        #endif
        #if DEBUG_STATE_CHANGES
        // state change => trace
        public readonly List<(string, string)> StateChanges = new();
        #endif

        private string failReason;
        /// <summary>
        /// Last fail reason, if any
        /// </summary>
        public string FailReason {
            get => failReason;
            set {
                // D.Warn("Setting fail reason for goal {0} -> {1}", value, this);
                taskLineFail = null;
                failReason = value;
            }
        }
        // Icon last passed to ChangeStateTo's failIcon while transitioning to Failed.
        // Used by RefreshFailIcon to redraw (or hide) it without waiting for the next
        // state transition.
        public string FailIcon { get; private set; }
        public bool IsForceAssigned => (Flags & AIGoalFlags.ForceAssigned) == AIGoalFlags.ForceAssigned;
        public bool IsStreamlined => (Flags & AIGoalFlags.Streamlined) == AIGoalFlags.Streamlined;
        public bool IsByPlayer => (Flags & AIGoalFlags.ByPlayer) == AIGoalFlags.ByPlayer;
        public bool IsNanobotCompatible => (Flags & AIGoalFlags.NanobotCompatible) == AIGoalFlags.NanobotCompatible;
        public bool IsNanobotWorkable => (Flags & AIGoalFlags.NanobotWorkable) == AIGoalFlags.NanobotWorkable;
        public bool IsNonInterruptable => (Flags & AIGoalFlags.NonInterruptable) == AIGoalFlags.NonInterruptable;
        public bool IsHidden => (Flags & AIGoalFlags.Hidden) == AIGoalFlags.Hidden;
        /// <summary>
        /// 0 - disabled, 1 - low, 3 - mid, 5 - top
        /// External value controlled by the player to micromanage goal priorities
        /// </summary>
        public byte GoalPriority = 3;
        private BInt goalPriorityB;
        public BInt GoalPriorityB => goalPriorityB ??= new BInt(GoalPriority);
        // Internal value used for comparing goals of same priority
        // Higher number = more important
        public byte GoalImportance;
        public AIGoalFlags Flags;
        [SkipDumpInstance]
        public FrugalArray<int> RequiredAbilities;
        /// <summary>
        /// Lower is more important
        /// </summary>
        [SkipDumpInstance]
        [System.NonSerialized] public int CurrentPriority;
        [SkipDumpInstance]
        [System.NonSerialized] public float PriorityTieBreaker;
        /// <summary>
        /// Used updating when to bid on it. Lower is more important.
        /// Use SetPriority instead.
        /// </summary>
        /// <param name="ticks"></param>
        /// <see cref="SetPriority"/>
        /// <returns>lower is higher priority</returns>
        public int UpdateInternalPriority(long ticks) {
            // Personal and combat goals only: the colony auction ranks colony goals by
            // AIAuctionIndex.RankFor. Small enough to only order goals of one priority
            PriorityTieBreaker = (ticks - StateStartTick) * 0.00001f;
            CurrentPriority =
                // Major octave
                (GoalImportance * -100)
                // Minor octave
                + (5 - GoalPriority);

            if (BoostedUntil > ticks) {
                CurrentPriority -= 5;
            }
            return CurrentPriority;
        }

        public AIGoal WithPathFlags(PathReqFlags flags) {
            PathFlags = flags;
            return this;
        }
        public AIGoal WithFlag(AIGoalFlags flag) {
            Flags |= flag;
            return this;
        }
        public AIGoal WithPriority(int priority) {
            GoalPriority = (byte) Maths.Clamp(priority, 0, 5);
            return this;
        }

        public AIGoal WithPriorityResolver(int id) {
            var res = Registries.GoalPriorityResolvers.FastGet(id);
            if (res == null) {
                D.Err("No priority resolver found for id {0} ({1})", id,
                    Game.CodeGen.PriorityResolverIdH.ResolveName(id));
            } else {
                PriorityResolver = res;
            }
            return this;
        }

        public AIGoal WithAllowedMindIds(uint[] ids) {
            AllowedMindIds = ids;
            return this;
        }
        public string IconId;
        public int JobIdH;
        private JobType job;
        public JobType Job {
            get {
                if (job != null) { return job; }
                if (JobIdH == 0) { return null; }
                if (JobType.TryGet(JobIdH, out job)) {
                    return job;
                }
                D.Err("Job not found by id {0} ({1}) for goal: {2}",
                    JobIdH, JobTypeIdH.ResolveName(JobIdH), this);
                return null;
            }
        }

        public uint Id;
        public AIBid BestBid;
        private B<AIGoalState> stateB;
        public B<AIGoalState> StateB => stateB ??= new B<AIGoalState>(State);
        public AIGoalState State { get; private set; }
        public AIGoalState StateBefore;
        public PathReqFlags PathFlags;
        public bool NonRetryable => (Flags & AIGoalFlags.NonRetryable) == AIGoalFlags.NonRetryable;
        public GoalPriorityResolver PriorityResolver;
        public AIGoalType Type;
        // Goals with higher interrupt priority cannot be interrupted by goals with
        // lower or equal interrupt priority
        /// <summary>
        /// 0 is for Idle, 1 is for normal / personal, 2 is combat, 3 is direct order
        /// </summary>
        public byte InterruptPriority { get; private set; }
        /// <summary>
        /// Minimum ticks the victim goal must have been InProgress before this goal
        /// is allowed to interrupt it. 0 means interrupt immediately (combat, emergencies).
        /// </summary>
        public int MinTicksBeforeInterrupt { get; private set; }
        public ushort TimesFailed;
        public ushort TimesFailedTotal;
        public uint FailedAgentId;
        public int NumBids;
        // AIAuction's per tick bookkeeping, never saved. Ticks start at -1 so tick 0 of a
        // new game does not look like "already done this tick"
        [SkipDumpInstance] public long AuctionCheckTick = -1;
        [SkipDumpInstance] public bool AuctionCheckOk;
        [SkipDumpInstance] public int AuctionSafetyPos;
        [SkipDumpInstance] public long AuctionWonTick = -1;
        [SkipDumpInstance] public long AuctionBidStamp;
        [SkipDumpInstance] public int AuctionBestBidder;
        // workers with no region path to it since its last bid
        [SkipDumpInstance] public byte RegPathMisses;
        // AIAuctionIndex: its bucket when it was last indexed for bidding
        [SkipDumpInstance] public int AuctionBucket;
        public void MarkFailed() {
            #if DEBUG_AI_GOALS
            D.Warn("Marking failed {0}", this);
            #endif
            TimesFailed++;
            TimesFailedTotal++;
        }
        public long CreatedAt;
        public long BoostedUntil;
        public long FailedAt;
        public long StateStartTick;
        public long CanRetryAfterTick;
        // When the doability gate last checked this goal on its way to or while in Bidding.
        // Not saved: a loaded goal waiting in Bidding re-checks on its first pass
        public long GateCheckTick;
        public string Name;
        public AIState WorldState;
        public AIState DesiredState;
        public AIState InitialState;
        public AIGoalCheck CheckDoable;
        [System.NonSerialized]
        public Func<GameState, AIGoal, bool> AvailabilityGate;
        public ComponentData Vars;
        public uint[] AllowedMindIds;
        public static uint LastId;

        public bool IsAvailabilityGateOpen(GameState state) {
            return IsForceAssigned || AvailabilityGate?.Invoke(state, this) != false;
        }
        /// <summary>
        /// A goal with OverwritePriority 2 in TrySetGoal will automatically cancel
        /// a goal with OverwritePriority 1 on the entity.
        /// </summary>
        public sbyte OverwritePriority;
        public AIGoalUI UI;

        #if !PRODUCTION_BUILD
        public Action<AIGoal, AIGoalState, AIGoalState> OnChangeState;
        #endif

        private AIGoal(AIGoalState state) {
            State = state;
            #if TRACE_AI_GOALS
            Trace = Environment.StackTrace;
            #endif
        }
        public static AIGoal CreateBlank(AIGoalState state) {
            return new AIGoal(state);
        }

        public AIGoal WithVar(string name, uint var) {
            Vars ??= new ComponentData(0, null);
            Vars.SetInt(name, (int) var);
            return this;
        }
        public bool TryGetVar(string name, out uint var) {
            if (Vars == null) {
                var = 0;
                return false;
            }
            return Vars.TryGetUint(name, out var);
        }

        public AIGoal WithVar(string name, int var) {
            Vars ??= new ComponentData(0, null);
            Vars.SetInt(name, var);
            return this;
        }
        public bool TryGetVar(string name, out int var) {
            if (Vars == null) {
                var = 0;
                return false;
            }
            return Vars.TryGetInt(name, out var);
        }

        public AIGoal WithVar(string name, string var) {
            Vars ??= new ComponentData(0, null);
            Vars.SetString(name, var);
            return this;
        }
        public bool TryGetVar(string name, out string var) {
            if (Vars == null) {
                var = null;
                return false;
            }
            return Vars.TryGetString(name, out var);
        }

        public string GoalIcon => IconId ?? JobIcon;

        private string JobIcon {
            get {
                if (JobIdH == 0) {
                    return Constants.IconId.CTask;
                }
                if (Job == null) {
                    D.Err("Job is null for ai goal: {0} with type hash: {1} ({2})",
                        this, JobIdH, JobTypeIdH.ResolveName(JobIdH));
                    return Constants.IconId.CTask;
                }
                return Job.IconId ?? Constants.IconId.CTask;
            }
        }
        public string JobNameT {
            get {
                if (JobIdH == 0) {
                    return T.JobTypeNeeds;
                }
                if (Job == null) {
                    D.Err("Job is null for ai goal: {0} with type hash: {1} ({2})",
                        this, JobIdH, JobTypeIdH.ResolveName(JobIdH));
                    return "Bug!";
                }
                return Job.NameT;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompleted(AIGoal goal) {
            return goal?.State == AIGoalState.Completed;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompleted(AIGoalState state) {
            return state == AIGoalState.Completed;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCancelled(AIGoal goal) {
            return goal?.State == AIGoalState.Cancelled;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCancelled(AIGoalState state) {
            return state == AIGoalState.Cancelled;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInterrupted(AIGoal goal) {
            return goal?.State == AIGoalState.Interrupted;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInterrupted(AIGoalState state) {
            return state == AIGoalState.Interrupted;
        }
        public static bool IsFailed(AIGoalState state) {
            switch (state) {
                case AIGoalState.Interrupted: return true;
                case AIGoalState.Failed: return true;
                case AIGoalState.Error: return true;
                case AIGoalState.ErrorLooping: return true;
                case AIGoalState.CoolingDown: return true;
                default: return false;
            }
        }
        public static bool IsFailed(AIGoal goal) {
            if (goal == null) { return false; }
            return IsFailed(goal.State);
        }
        /// <summary>
        /// Available + Failed
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsAvailable(AIGoal goal) {
            if (goal == null) { return false; }
            return IsAvailable(goal.State);
        }

        /// <summary>
        /// Available + Failed
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsAvailable(AIGoalState state) {
            switch (state) {
                case AIGoalState.Available: return true;
                case AIGoalState.Bidding: return true;
                case AIGoalState.Failed: return true;
                case AIGoalState.Interrupted: return true;
                case AIGoalState.Pending: return true;
                default: return false;
            }
        }
        public static bool IsArchived(AIGoalState state) {
            switch (state) {
                case AIGoalState.Completed: return true;
                case AIGoalState.Cancelled: return true;
                case AIGoalState.Error: return true;
                case AIGoalState.ErrorLooping: return true;
                default: return false;
            }
        }
        public static bool IsActive(AIGoalState state) {
            switch (state) {
                case AIGoalState.PreAssigned: return true;
                case AIGoalState.InProgress: return true;
                case AIGoalState.Available: return true;
                case AIGoalState.Pending: return true;
                case AIGoalState.Bidding: return true;
                default: return false;
            }
        }

        public static bool IsActive(AIGoal goal) {
            if (goal == null) { return false; }
            return IsActive(goal.State);
        }

        /// <summary>
        /// Colony work may not be executed on an entity that is on fire;
        /// extinguishing the fire is the only allowed action for such entities.
        /// </summary>
        public static bool IsBlockedByFire(AIGoal goal) {
            return goal.JobIdH != JobTypeIdH.Firefighting
                && goal.Target?.Entity?.Flammable?.IsOnFire == true;
        }

        public static AIGoal Idle(AIAgentComp agent, long ticks) {
            var g = agent.IdleGoal;
            if (g != null) {
                if (g.WorldState.VarCount == 1) {
                    g.WorldState.Set(AIVarsH.IsIdle, false);
                } else {
                    // Some goal has tainted the world state, let's reset it completely
                    g.WorldState = AIState.With(AIVarsH.IsIdle, false);
                }
                g.InterruptPriority = 0;
                g.ChangeStateTo(AIGoalState.InProgress, ticks, true);
            } else {
                g = new AIGoal(AIGoalState.InProgress) {
                    Id = ++LastId,
                    Name = T.Idle,
                    Agent = agent,
                    Type = AIGoalType.Idle,
                    CreatedAt = ticks,
                    StateStartTick = ticks,
                    InterruptPriority = 0,
                    WorldState = AIState.Create(),
                    DesiredState = AIState.Create(),
                }.WithWorldToDesired(AIVarsH.IsIdle, false, true);
                agent.IdleGoal = g;
            }
            return g;
        }
        public static AIGoal CreatePersonal(int jobTypeH, string icon, string name,
                AIAgentComp agent, long ticks) {
            var goal = new AIGoal(AIGoalState.Available) {
                Id = ++LastId,
                Name = name,
                IconId = icon,
                Agent = agent,
                Type = AIGoalType.Personal,
                JobIdH = jobTypeH,
                InterruptPriority = 1,
                CreatedAt = ticks,
                StateStartTick = ticks,
                WorldState = AIState.Create(),
                DesiredState = AIState.Create(),
            };
            agent.Goals.Add(goal);
            return goal;
        }
        public static AIGoal CreateCombat(string name, CombatUnit unit, long ticks) {
            var goal = new AIGoal(AIGoalState.Available) {
                Id = ++LastId,
                Name = name,
                Agent = unit.Agent,
                Type = AIGoalType.Combat,
                InterruptPriority = 2,
                CreatedAt = ticks,
                StateStartTick = ticks,
                GoalImportance = 1,
                WorldState = AIState.Create(),
                DesiredState = AIState.Create(),
            };
            unit.Goals.Add(goal);
            return goal;
        }
        public static AIGoal DirectOrder(int jobTypeH, string iconId, string name,
                AIAgentComp agent, long ticks) {
            var goal = new AIGoal(AIGoalState.PreAssigned) {
                JobIdH = jobTypeH,
                Id = ++LastId,
                Name = name,
                Agent = agent,
                IconId = iconId,
                Flags = AIGoalFlags.ForceAssigned | AIGoalFlags.NonRetryable,
                CreatedAt = ticks,
                StateStartTick = ticks,
                InterruptPriority = 3,
                Type = AIGoalType.Personal,
                WorldState = AIState.Create(),
                DesiredState = AIState.Create(),
            };
            return goal;
        }
        public static AIGoal Create(int jobTypeH,
                string name, IAIGoalProvider target, long ticks) {
            var goal = new AIGoal(AIGoalState.Pending) {
                Id = ++LastId,
                Name = name,
                Target = target,
                CreatedAt = ticks,
                StateStartTick = ticks,
                InterruptPriority = 1,
                JobIdH = jobTypeH,
                WorldState = AIState.Create(),
                DesiredState = AIState.Create(),
            };
            return goal;
        }

        public static bool TryRetry(AIGoal goal, long ticks, bool force, bool boost) {
            if (goal == null) { return false; }
            if (boost) {
                goal.Boost();
            }
            switch (goal.State) {
                case AIGoalState.Failed:
                case AIGoalState.Error:
                case AIGoalState.ErrorLooping: {
                    if (force) {
                        goal.ClearCounters();
                    } else if (ticks < goal.CanRetryAfterTick) {
                        // can't retry yet
                        return false;
                    }
                    goal.ChangeStateTo(AIGoalState.Pending, ticks, true);
                    goal.UpdateIcon();
                    return true;
                }
            }
            return false;
        }

        public void ClearCounters() {
            #if DEBUG_AI_GOALS
            D.Warn("Clearing counters for {0}", this);
            #endif
            TimesFailed = 0;
            CanRetryAfterTick = 0;
            StateBefore = AIGoalState.Uninitialized;
        }

        /// <summary>
        /// It is expected for agent to not be null, but if it is null it won't
        /// be set. Use SetAgent(null) to set it to null.
        /// </summary>
        /// <param name="agent"></param>
        /// <returns>true if agent is capable and it was set</returns>
        public bool TrySetAgent(AIAgentComp agent) {
            if (agent == null) { return false; }
            if (!CanBeDoneBy(agent)) {
                D.Err("Goal cannot be done by worker: {0} -> {1}", this, agent);
                return false;
            }
            if (Agent != null) {
                if (Agent == agent) {
                    D.Err("Trying to set agent to itself: {0} | {1}", this, agent);
                    return true;
                }
                D.Err("Trying to overwrite the agent on goal: {0} | {1} -> {2}",
                    this, Agent, agent);
                return false;
            }
            Agent = agent;
            return true;
        }

        // Escalating retry delay for a failed goal. An explicit retryDelay overrides
        // the escalation (gate cadences). Every failure path, including the auction's,
        // goes through here so a failed goal always has a CanRetryAfterTick.
        public static void SetRetryBackoff(AIGoal goal, long ticks, int retryDelay = 0) {
            if (retryDelay == 0) {
                if (goal.Type == AIGoalType.Colony) {
                    // https://www.desmos.com/calculator/b5kweqf25g
                    retryDelay = (30 + 2 * goal.TimesFailed * goal.TimesFailed)
                        * Consts.TicksPerMinute;
                    if (retryDelay > Consts.TicksPer2Hours) {
                        retryDelay = Consts.TicksPer2Hours;
                    }
                } else { // personal, etc
                    // https://www.desmos.com/calculator/067ywuozka
                    retryDelay = (10 + 8 * goal.TimesFailed)
                        * Consts.TicksPerMinute;
                    if (retryDelay > Consts.TicksPerHour) {
                        retryDelay = Consts.TicksPerHour;
                    }
                }
            }
            goal.CanRetryAfterTick = ticks + retryDelay;
        }

        public void SetAgent(AIAgentComp agent) {
            if (agent == null && Type != AIGoalType.Idle) {
                if (State == AIGoalState.InProgress) {
                    D.Err("Setting null agent on a goal that is in progress or preassigned. Interrupt or cancel it first! {0} | {1}",
                        this, Agent);
                    StateBefore = State;
                    State = AIGoalState.Interrupted;
                    TraceStateChange(StateBefore, State);
                    ResetWorldState();
                } else if (State == AIGoalState.PreAssigned) {
                    D.Warn("Setting null agent on a goal that is preassigned. Shifting state to available! {0} | {1}",
                        this, Agent);
                    StateBefore = State;
                    State = AIGoalState.Available;
                    TraceStateChange(StateBefore, State);
                    ResetWorldState();
                }
            }
            Agent = agent;
            UI?.Rebuild();
        }

        public AIGoal WithWorld(int key, bool val) {
            WorldState.Set(key, val);
            return this;
        }
        public AIGoal WithDesired(int key, bool val) {
            DesiredState.Set(key, val);
            return this;
        }

        public AIGoal WithIcon(string iconId) {
            IconId = iconId;
            return this;
        }

        public AIGoal WithoutRetry() {
            Flags |= AIGoalFlags.NonRetryable;
            return this;
        }
        public AIGoal NonInterruptable() {
            Flags |= AIGoalFlags.NonInterruptable;
            return this;
        }
        public AIGoal ForceAssigned() {
            Flags |= AIGoalFlags.ForceAssigned;
            return this;
        }

        public AIGoal WithWorldToDesired(int key, bool valFrom, bool valTo) {
            WorldState.Set(key, valFrom);
            DesiredState.Set(key, valTo);
            return this;
        }
        public override string ToString() {
            string stateStr;
            if (AIGoal.IsFailed(State)) {
                stateStr = $"{StateName(State)}|{failReason}";
            } else {
                stateStr = StateName(State);
            }
            #if TRACE_AI_GOALS
            if (Target?.Entity != null) {
                return $"AIGoal[{Type}|Im:{GoalImportance},Pr:{GoalPriority},IP:{InterruptPriority}|{Name};{stateStr};{WorldState};{DesiredState};Agent:{Agent};Target:{Pos.String(Target.Entity.PosIdx)}{Target};Fl:{Flags};Trace:{Trace}";
            }
            return $"AIGoal[{Type}|Im:{GoalImportance},Pr:{GoalPriority},IP:{InterruptPriority}|{Name};{stateStr};{WorldState};{DesiredState};Agent:{Agent};Target:{Target};Fl:{Flags};Trace:{Trace}";
            #endif
            if (Target?.Entity != null) {
                return $"AIGoal[{Type}|Im:{GoalImportance},Pr:{GoalPriority},IP:{InterruptPriority}|{Name};{stateStr};{WorldState};{DesiredState};Agent:{Agent};Target:{Pos.String(Target.Entity.PosIdx)}{Target};Fl:{Flags}";
            }
            return $"AIGoal[{Type}|Im:{GoalImportance},Pr:{GoalPriority},IP:{InterruptPriority}|{Name};{stateStr};{WorldState};{DesiredState};Agent:{Agent};Target:{Target};Fl:{Flags}";
        }

        public bool CheckDone(long ticks) {
            if (DesiredState.IsFulfilledIn(WorldState)) {
                ChangeStateTo(AIGoalState.Completed, ticks);
                return true;
            }
            return false;
        }

        public void ChangeStateTo(AIGoalState newState, long ticks,
                bool ignoreSame = false,
                string failIcon = Constants.IconId.CWarning) {
            var oldState = State;

            #if DEBUG_AI_GOALS
            D.Warn("Changing goal state: {0} -> {1} {2}", oldState, newState, this);
            #endif

            if (oldState != newState) {
                StateBefore = State;
                State = newState;
                #if DEBUG_STATE_CHANGES
                TraceStateChange(StateBefore, State);
                #endif
                StateStartTick = ticks;
                if (IsFailed(State)) {
                    LogFailure();
                }
                if (Agent != null && (IsCancelled(State) || IsInterrupted(State))) {
                    // We must not clear the goal on the agent if the goal is not == this!
                    if (Agent.Goal == this) {
                        // prevent situations when transition between actions gets interrupted
                        // and BB does not get cleared
                        if (Agent.CurrentAction == null) {
                            AISys.EnsureCurrentAction(Agent, ticks, this);
                        }
                        Agent.ClearGoal();
                    }
                    Agent = null;
                }
                bool isCompleted = IsCompleted(State);
                if (isCompleted && Agent != null && Agent.BeingData.Mind.Health != null) {
                    // reward / punish the agent for doing tasks they love / hate
                    if (Agent.Jobs.TryJob(JobIdH, out var jobSlot)) {
                        if (jobSlot.Loves) {
                            var effect = MentalEffect.CreateWithIcon(
                                Agent.PosIdx,
                                Constants.IconId.CHeart,
                                ticks, Consts.TicksPer2Hours, $"{T.VerbLoves}: {jobSlot.Job.NameT}", 3);
                            Agent.BeingData.Mind.Health.AddEffect(Agent.S, effect);
                        }
                    }
                }
                if (Target != null && Target.Entity.IsActive) {
                    if (isCompleted || IsCancelled(State)) {
                        Target.Entity.UnsetGoal(this);
                        Target.HideInfoIcon();
                    } else if (IsFailed(State)) {
                        ResetWorldState();
                        FailedAt = ticks;
                        FailIcon = failIcon;
                        if (Target != null && Target.Entity.IsActive) {
                            if (AIGoalUI.IsFailureSuppressed(this)) {
                                Target.HideInfoIcon();
                            } else if (failIcon == null) {
                                if (Type == AIGoalType.Colony) {
                                    D.Err("Refusing to hide fail icon on a colony goal: {0}", this);
                                    Target.ShowInfoIcon(Constants.IconId.CWarning, null);
                                } else {
                                    Target.HideInfoIcon();
                                }
                            } else {
                                Target.ShowInfoIcon(failIcon, null);
                            }
                        }
                        UI?.Rebuild();
                    } else {
                        UpdateIcon();
                    }
                    Target.OnGoalChange(this, oldState, newState);
                }
                #if !PRODUCTION_BUILD
                if (OnChangeState != null) {
                    OnChangeState.Invoke(this, oldState, newState);
                }
                #endif
                stateB?.Set(newState);
            } else {
                if (!ignoreSame) {
                    #if DEBUG_AI_GOALS
                    D.Warn("Goal is already in state {0}: {1}", newState, this);
                    #endif
                }
            }
        }


        // The fail reason last written to the log. A goal that keeps failing for the
        // same reason (retries) is logged once, not on every attempt.
        private string loggedFailReason;

        // Task failures used to show only as a passing tooltip or an icon on the target, so a
        // player looking at a colonist's or a device's LOG tab could not tell why the work
        // was not getting done
        private void LogFailure() {
            // Hidden tasks are background chores the player never asked for; a need
            // interrupting one is not worth a line in the colonist's log
            if (IsHidden) { return; }
            var reason = failReason;
            if (string.IsNullOrEmpty(reason) || reason == loggedFailReason) { return; }
            if (AIGoalUI.IsFailureSuppressed(this)) { return; }
            var line = $"{Name}: {Texts.Red(reason)}";
            // Colony work failures also go to the target's log, so the device or object shows
            // why it is not being serviced. Personal need targets (meals, beds) would be noise.
            var target = Target?.Entity;
            bool logOnTarget = Type == AIGoalType.Colony && target != null && target.IsActive;
            var being = Agent?.Being;
            if (being != null) {
                if (!Agent.BeingData.IsPlayer) { return; }
                loggedFailReason = reason;
                if (logOnTarget) {
                    Agent.S.Sys.Log.AddGoalFailure(being, target, line);
                } else {
                    Agent.S.Sys.Log.AddLine(being, line);
                }
            } else if (logOnTarget) {
                // Failed before any worker took it (e.g. lacking materials)
                loggedFailReason = reason;
                target.S.Sys.Log.AddEntityOnlyLine(target, line);
            }
        }

        public void SetPriority(long ticks, int newPriority, bool allowZero = true) {
            if (!allowZero && newPriority < 1) {
                newPriority = 1;
            } else if (newPriority > 5) {
                newPriority = 5;
            }
            if (newPriority < 0) {
                newPriority = 0;
            }
            if (newPriority == GoalPriority) { return; }
            var needsRetry = newPriority > GoalPriority && IsFailed(State);
            GoalPriority = (byte) newPriority;
            goalPriorityB?.Set(newPriority);
            if (Target is IAIGoalPriorityListener ap) {
                ap.OnAIGoalPriorityChange(this);
            }
            if (needsRetry) {
                // if we are increasing priority on a rejected task, we may want
                // to retry immediately
                ClearCounters();
                ChangeStateTo(AIGoalState.Available, ticks, true);
            }
        }

        public void AdjustPriority(long ticks, sbyte adj, bool allowZero = true) {
            var newPriority = GoalPriority + adj;
            if (adj > 0) {
                Boost();
            } else {
                BoostedUntil = 0;
            }
            SetPriority(ticks, newPriority, allowZero);
        }

        public void Boost() {
            BoostedUntil = G.Ticks + Tunables.NewTaskBoostMinutes;
        }

        public void ClearVars() {
            Vars = null;
        }

        public AIGoal WriteInitialState() {
            InitialState = AIState.Create();
            WorldState.CopyTo(ref InitialState);
            return this;
        }

        public void ResetWorldState() {
            // let's not treat retried goals as streamlined
            Flags &= ~AIGoalFlags.Streamlined;
            if (InitialState.Vars != null) {
                InitialState.CopyTo(ref WorldState);
            } else {
                if (WorldState.VarCount != DesiredState.VarCount) {
                    D.Err("WorldState and DesiredState has non-same variable count for goal! use WriteInitialState()!: Initial: {0} | World: {1} | Desired: {2} | Goal: {3}",
                        InitialState, WorldState, DesiredState, this);
                }
                // we need to invert the DesiredState to get the initial worldState
                DesiredState.InvertCopyTo(ref WorldState);
            }
        }
        public void UpdateIcon() {
            if (IconId != null) {
                if (Target != null && Target.Entity.IsActive) {
                    Target.ShowInfoIcon(IconId, null);
                }
            }
        }
        // Re-applies (or re-hides) the current fail icon without waiting for the next
        // state transition; used when a provider's suppression preference changes while
        // the goal is still Failed (e.g. toggling CraftingOrder.SuppressDeficitWarning).
        public void RefreshFailIcon() {
            if (!IsFailed(State)) { return; }
            if (Target == null || !Target.Entity.IsActive) { return; }
            if (AIGoalUI.IsFailureSuppressed(this)) {
                Target.HideInfoIcon();
            } else if (FailIcon != null) {
                Target.ShowInfoIcon(FailIcon, null);
            }
        }
        public static void Fail(AIGoal goal, long ticks, string reason) {
            goal.FailReason = reason;
            goal.ChangeStateTo(AIGoalState.Failed, ticks, true);
        }
        public static bool Interrupt(AIGoal goal, long ticks, string reason = null) {
            if (goal == null) { return true; }
            if (goal.IsNonInterruptable && !goal.Agent.IsDisabled) {
                D.Warn("Trying to interrupt a non-interruptable goal! {0}", goal);
                return false;
            }
            #if !PRODUCTION_BUILD
            if (!The.TestsAreRunning) {
                D.Log("Interrupting goal: {0}, {1}, {2}", goal, ticks, reason);
            }
            #endif
            goal.FailReason = reason;
            goal.ChangeStateTo(AIGoalState.Interrupted, ticks, true);
            return true;
        }

        /// <summary>
        /// It will also remove entity.Goal
        /// Use removeImmediately = false if calling from AIActions or AIPlannerSys
        /// </summary>
        public static void Cancel(AIGoal goal, long ticks, string reason = null,
                bool removeImmediately = true) {
            if (goal == null) {
                D.Err("Trying to cancel a null goal!");
                return;
            }
            goal.FailReason = reason;
            goal.ClearCounters();
            goal.ChangeStateTo(AIGoalState.Cancelled, ticks, true);
            if (removeImmediately && goal.Type == AIGoalType.Colony) {
                The.R.Sig.AIRemoveGoal.Send(goal);
            }
        }
        public static string StateNameSimple(AIGoalState state) {
            switch (state) {
                case AIGoalState.Uninitialized:
                case AIGoalState.Available:
                case AIGoalState.Pending:
                case AIGoalState.Bidding: return Texts.Yellow(T.WaitingForWorker);
                case AIGoalState.PreAssigned:
                case AIGoalState.InProgress: return Texts.Green(T.GoalStateInprogress);
                case AIGoalState.Failed: return Texts.Red(T.GoalStateFailed);
                case AIGoalState.CoolingDown: {
                    #if PRODUCTION_BUILD
                    return Texts.Yellow(T.GoalStateFailed);
                    #endif
                    return Texts.Yellow("Cooling Down");
                }
                case AIGoalState.Cancelled: return Texts.Yellow(T.GoalStateCancelled);
                case AIGoalState.Completed: return Texts.Green(T.GoalStateCompleted);
                case AIGoalState.Error: return Texts.Red(T.GoalStateError);
                case AIGoalState.ErrorLooping: return Texts.Red(T.GoalStateErrorlooping);
                case AIGoalState.Interrupted: return Texts.Red(T.GoalStateInterrupted);
            }
            D.Err("Unhandled goal state: {0}", state);
            return state.ToString();
        }

        public static string StateName(AIGoalState state) {
            switch (state) {
                case AIGoalState.Available: return T.GoalStateAvailable;
                case AIGoalState.Bidding: return T.GoalStateBidding;
                case AIGoalState.InProgress: return T.GoalStateInprogress;
                case AIGoalState.Pending: return T.GoalStatePending;
                case AIGoalState.Failed: return T.GoalStateFailed;
                case AIGoalState.CoolingDown: {
                    #if PRODUCTION_BUILD
                    return T.GoalStateFailed;
                    #endif
                    return "Cooling Down";
                }
                case AIGoalState.Uninitialized: return T.GoalStateUninitialized;
                case AIGoalState.Cancelled: return T.GoalStateCancelled;
                case AIGoalState.Completed: return T.GoalStateCompleted;
                case AIGoalState.Error: return T.GoalStateError;
                case AIGoalState.ErrorLooping: return T.GoalStateErrorlooping;
                case AIGoalState.PreAssigned: return T.GoalStatePreassigned;
                case AIGoalState.Interrupted: return T.GoalStateInterrupted;
            }
            D.Err("Unhandled goal state: {0}", state);
            return state.ToString();
        }

        public AIGoal ByPlayer(bool yes) {
            if (yes) {
                Flags |= AIGoalFlags.ByPlayer;
            } else {
                Flags &= ~AIGoalFlags.ByPlayer;
            }
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAllowedFor(AIAgentComp agent) {
            if (AllowedMindIds == null || AllowedMindIds.Length == 0) {
                return true;
            }
            return agent?.BeingData?.Mind != null
                && Array.IndexOf(AllowedMindIds, agent.BeingData.Mind.Id) != -1;
        }

        public bool CanBeDoneBy(AIAgentComp agent) {
            if (agent == null) {
                return false;
            }
            if (!IsAllowedFor(agent)) {
                return false;
            }
            if (agent.BeingData.SkipSimulate) {
                D.Warn("Agent skip simulate: {0}", agent);
                return false;
            }
            if (!agent.BeingData.Body.IsAbleToWork) {
                D.Warn("Agent cannot work: {0}", agent);
                return false;
            }
            if (agent.Goal?.IsNonInterruptable == true) {
                return false;
            }
            if (JobIdH == 0) { return true; }
            if (!agent.Jobs.CanDo(JobIdH)) {
                D.Warn("Cannot do job {0}: {1}",
                    JobTypeIdH.ResolveName(JobIdH), agent);
                return false;
            }
            return true;
        }

        private string taskLineFail;
        private string taskLine;
        public AIActionResult LastResult;

        /// <summary>
        /// Used in thought bubble of the ai agent when agent fails a task
        /// </summary>
        public string TaskLineFail {
            get {
                if (taskLineFail == null) {
                    taskLineFail = T.AdvertRejectInfo_V3.F(
                        JobNameT, Name, FailReason);
                }
                return taskLineFail;
            }
        }

        public string TaskLine {
            get {
                if (JobIdH == 0) {
                    return Name;
                }
                if (taskLine == null) {
                    taskLine = T.AdvertTask_V2.F(JobNameT, Name);
                }
                return taskLine;
            }
        }

        public void LoadState(AIGoalState state) {
            State = state;
        }

        public void ReattachTo(IAIGoalProvider target) {
            Target.Entity.UnsetGoal();
            Target = target;
            target.Entity.TrySetGoal(this, addToAISystem: false);
        }

        public void Validate(object ctx) {
            if (InitialState.Vars == null) {
                if (WorldState.VarCount != DesiredState.VarCount) {
                    D.Err("WriteInitialVars() for this goal! WS: {0}: DS: {1}: {2}: {3}",
                        WorldState, DesiredState, this, ctx);
                    DumpStateTrace();
                }
            } else {
                if (WorldState.VarCount != InitialState.VarCount) {
                    D.Err("ResetWorldState() for this goal! WS: {0}: IS: {1}: {2}: {3}. Target: {4} | {5}",
                        WorldState, InitialState, this, ctx, Target, Target?.Entity);
                    DumpStateTrace();
                    ResetWorldState();
                    if (WorldState.VarCount != InitialState.VarCount) {
                        D.Err("ResetWorldState() DID NOT HELP for this goal! WS: {0}: IS: {1}: {2}: {3}",
                            WorldState, InitialState, this, ctx);
                    }
                }
            }
        }

        public AIGoal WithOverwritePriority(sbyte v) {
            OverwritePriority = v;
            return this;
        }

        /// <summary>
        /// Sets the interrupt priority and the minimum number of ticks the victim goal
        /// must have been InProgress before this goal is allowed to interrupt it.
        /// Use 0 for minTicksBeforeInterrupt on combat/emergency goals for immediate interrupts.
        /// </summary>
        public AIGoal WithInterrupt(int priority, int minTicksBeforeInterrupt) {
            InterruptPriority = (byte) priority;
            MinTicksBeforeInterrupt = minTicksBeforeInterrupt;
            return this;
        }

        public bool ShouldInterrupt(AIGoal curGoal, long ticks) {
            if (curGoal == null) { return true; }
            if (curGoal == this) { return false; }
            if (InterruptPriority <= curGoal.InterruptPriority) {
                return false;
            }
            if (GoalImportance >= curGoal.GoalImportance && GoalPriority >= curGoal.GoalPriority) {
                if (curGoal.IsNonInterruptable) {
                    return false;
                }
                if (MinTicksBeforeInterrupt > 0
                        && ticks - curGoal.StateStartTick < MinTicksBeforeInterrupt) {
                    // not urgent enough yet: let the current short task finish on its own
                    #if DEBUG_AI_GOALS
                    D.Warn("Goal {0} not interrupting {1} yet - {2} ticks left before interrupt. Being {3}",
                        this.TaskLine, curGoal.TaskLine, MinTicksBeforeInterrupt - (ticks - curGoal.StateStartTick), curGoal.Agent);
                    #endif
                    return false;
                } else {
                    #if DEBUG_AI_GOALS
                    D.Warn("Goal {0} interrupting {1}. Goal executed for {2} ticks, interrupt after {3}. Being: {4}",
                        this.TaskLine, curGoal.TaskLine, ticks - curGoal.StateStartTick, MinTicksBeforeInterrupt, curGoal.Agent);
                    #endif
                }
                #if DEBUG_AI_GOALS
                D.Warn("Interrupting goal: {0} | Interrupted BY {1}", goal, this);
                #endif
                return true;
            }
            return false;
        }

        public AIGoal NanobotCompatible() {
            Flags |= AIGoalFlags.NanobotCompatible;
            return this;
        }
        public AIGoal WithRequiredAbilities(int a1) {
            RequiredAbilities = new FrugalArray<int>(a1);
            return this;
        }

        public AIGoal WithRequiredAbilities(int a1, int a2) {
            RequiredAbilities = new FrugalArray<int>(a1, a2);
            return this;
        }
        public AIGoal WithRequiredAbilities(int a1, int a2, int a3) {
            RequiredAbilities = new FrugalArray<int>(a1, a2, a3);
            return this;
        }
        public AIGoal WithRequiredAbilities(FrugalArray<int> abilities) {
            RequiredAbilities = abilities;
            return this;
        }

        [Conditional("UNITY_EDITOR")]
        public void DumpStateTrace() {
            #if DEBUG_STATE_CHANGES
            D.Err("Goal state trace: {0} | {1}", this, D.DumpCollection(StateChanges));
            #else
            D.Err("uncomment #define DEBUG_STATE_CHANGES to see the trace");
            #endif
        }
        [Conditional("UNITY_EDITOR")]
        private void TraceStateChange(AIGoalState before, AIGoalState after) {
            #if DEBUG_STATE_CHANGES
            StateChanges.Add(($"{before}->{after} | T: {G.Ticks} | W:{Agent?.Being.Id ?? 0}", Environment.StackTrace));
            #endif
        }
    }
}
