#if !PRODUCTION_BUILD
// #define DEBUG_COOLDOWNS
// #define DEBUG_COOLDOWN_CHECK
// #define DEBUG_BB
// #define TRACE_MOVE_PROGRESS
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Backstories;
using Game.Systems.Drama;
using Game.Systems.Items;
using Game.Systems.Motion;
using Game.Systems.Notify;
using Game.Systems.Path;
using Game.Utils;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public sealed class AIAgentBlackboard {
        [Key("Byte1")] public byte Byte1;
        [Key("Int1")] public int Int1;
        [Key("Bool1")] public bool Bool1;
        [Key("Float1")] public float Float1;
        [Key("TargetEntityId")] public int TargetEntityId;
        [Key("TargetTileId")] public int TargetTileId;
        [Key("TargetBeingId")] public int TargetBeingId;
        [Key("TargetObjDataId")] public uint TargetObjDataId;
        [Key("TargetMatTypeId")] public int TargetMatTypeId;
        [Key("Cooldowns")] public Dictionary<int, long> Cooldowns;
        /// <summary>
        /// for save-load only
        /// </summary>
        [Key("GoalNeedIds")] public Dictionary<uint, int> GoalNeedIds;
        [Key("Blacklist")] public HashSet<int> Blacklist;
        [Key("Blacklist2")] public HashSet<int> Blacklist2;
        [Key("BlacklistUint")] public HashSet<uint> BlacklistUint;
        [Key("Long1")] public long Long1;
        [IgnoreMember] public readonly Dictionary<int, AIGoal> NeedGoals = new();
        [IgnoreMember] public AIGoal ReservedSlotGoal { get; private set; }
        [IgnoreMember] public Slot ReservedSlot { get; private set; }
        // not slot, but spot! for tables with sides and chairs
        [IgnoreMember] public int ReservedSideIdx { get; private set; } = -1;
        [IgnoreMember] public SocialOpportunity Opportunity;
        /// <summary>
        /// An animated sprite set can be created and attached to the being
        /// for situations like "reinstalling a device"
        /// </summary>
        [IgnoreMember] public AnimatedSpriteSet FauxObj { get; private set; }
        [IgnoreMember] public ObjData TargetObjData;
        [IgnoreMember] public SocialChatContext ChatContext;
        #if DEBUG_BB
        [IgnoreMember] private Entity targetEntity;
        [IgnoreMember] public Entity TargetEntity {
            get => targetEntity;
            set => D.SetFieldWithDebug(ref targetEntity, value, "TargetEntity");
        }
        #else
        [IgnoreMember] public Entity TargetEntity;
        #endif
        [IgnoreMember] public Tile TargetTile;
        [IgnoreMember] public Being TargetBeing;
        [IgnoreMember] public bool Bool1NotSaved;
        [IgnoreMember] public bool Bool2NotSaved;
        [IgnoreMember] public bool Bool3NotSaved;
        [IgnoreMember] public float Float1NotSaved;
        [IgnoreMember] public float Float2NotSaved;
        [IgnoreMember] public long Long1NotSaved;
        [IgnoreMember] public int Int1NotSaved;
        [IgnoreMember] public float BloodLossUntilBleedL;
        [IgnoreMember] public MatType TargetMatType;
        [IgnoreMember] private AIAgentComp agent;
        [IgnoreMember] public ParticlesVFX FX;
        [IgnoreMember] public ParticlesSys.Data Particles { get; private set; }
        [IgnoreMember] public double NextSoundTime;

        // Temporal
        [IgnoreMember] public MotionProgress MoveProgress;
        [IgnoreMember] public int MoveTargetPos;
        [IgnoreMember] public bool IsTargetReachable;
        [IgnoreMember] public bool IsWaitingForPathEstimate;
        [IgnoreMember] public int WaitingForEstimateTicks;
        [IgnoreMember] public bool HasRequestedReachability;
        [IgnoreMember] public NotificationHandle Notification;
        [IgnoreMember] public object Object1;
        [IgnoreMember] public object Object2;
        private readonly List<MatReservation> activeReservations = new(4);
        private readonly Dictionary<int, (float, long)> actionCostCache = new();
        /// <summary>
        /// Per-worker, per-goal cooldown. Non-serialized — short-lived, cleared on respawn.
        /// Keyed by <see cref="AIGoal.Id"/> (uint), distinct from <see cref="Cooldowns"/> (keyed by action hash int).
        /// Set by actions via <see cref="SetGoalCooldown"/> when this worker can't do a goal
        /// (e.g. storage unreachable) so the worker skips re-bidding it for a while.
        /// Full sweep cleanup is triggered lazily from ClearGoal every 4 in-game hours.
        /// </summary>
        [IgnoreMember] private Dictionary<uint, long> goalCooldowns;
        [IgnoreMember] private long nextGoalCooldownCleanTick;

        public AIAgentBlackboard() {
            Cooldowns = new();
        }

        public void SetReservation(MatReservation res) {
            // D.Warn("Adding reservation: {0}", res);
            // Check for duplicate (same storage + type)
            for (int i = 0; i < activeReservations.Count; i++) {
                var existing = activeReservations[i];
                if (existing.Storage == res.Storage && existing.Type == res.Type) {
                    D.Err("Trying to add duplicate reservation! Existing: {0}, New: {1} | Agent: {2}",
                        existing, res, agent);
                    return;
                }
            }
            activeReservations.Add(res);
        }

        public void RemoveReservation(MatReservation res) {
            // D.Warn("Removing reservation: {0}", res);
            // Not found - might be managed by action's own tracking (multi-material)
            // This is not an error for multi-reservation scenarios
            activeReservations.Remove(res);
        }
        public void SetAgent(AIAgentComp agent) {
            this.agent = agent;
        }

        public bool SetTargetObj(ObjData data, AIGoal goal) {
            if (TargetObjData != null && TargetObjData.Reservation?.Goal != null) {
                TargetObjData.TryUnreserve(goal);
            }
            if (data == null) {
                TargetObjData = null;
                return true;
            }
            if (data.TryReserve(goal)) {
                TargetObjData = data;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryNeedGoal(int needId, out AIGoal goal) {
            if (needId == 0) {
                D.Err("Trying to get need id 0 goal! {0}");
                goal = null;
                return false;
            }
            return NeedGoals.TryGetValue(needId, out goal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNeedGoal(int needId, AIGoal goal) {
            if (needId == 0) {
                D.Err("Trying to set need id 0 goal! {0}", goal);
                return;
            }
            NeedGoals[needId] = goal;
        }

        public void SetCooldown(int id, long ticks, int expireTicksFrom, int expireTicksTo) {
            #if DEBUG_COOLDOWNS
            D.Warn("Setting cooldown: {0} ({1}-{2}) | Act: {3}, Goal: {4}, Agent: {5}", id,
                Units.TicksToTime(expireTicksFrom), Units.TicksToTime(expireTicksTo),
                agent.CurrentAction, agent.Goal, agent);
            #endif
            Cooldowns[id] = ticks + Rng.URange(expireTicksFrom, expireTicksTo);
        }

        public void ClearFX() {
            if (FX != null) {
                if (FX.TryGetFX(out var fx)) {
                    fx.SetFloat(ShaderVars.EmitRate, 0f);
                }
                FX.DestroyAfterRealTime(5f);
                FX = null;
            }
        }

        /// <summary>
        /// Removes all expired goal cooldown entries. Called lazily from ClearGoal every
        /// 4 in-game hours to bound dict size without per-tick overhead.
        /// </summary>
        internal void MaybeCleanGoalCooldowns(long ticksNow) {
            if (ticksNow < nextGoalCooldownCleanTick) { return; }
            nextGoalCooldownCleanTick = ticksNow + Consts.TicksPer4Hours;
            if (goalCooldowns == null || goalCooldowns.Count == 0) { return; }
            var toRemove = Caches.UIntList;
            toRemove.Clear();
            foreach (var kv in goalCooldowns) {
                if (ticksNow > kv.Value) {
                    toRemove.Add(kv.Key);
                }
            }
            for (int i = toRemove.Count - 1; i >= 0; i--) {
                goalCooldowns.Remove(toRemove[i]);
            }
            toRemove.Clear();
        }

        /// <summary>
        /// Records a cooldown so this agent won't bid on <paramref name="goalId"/> for a random
        /// duration between <paramref name="expireTicksFrom"/> and <paramref name="expireTicksTo"/>.
        /// Use when the agent knows it cannot currently service a goal (e.g. storage unreachable)
        /// so that another worker can pick it up without this one hogging the bid every tick.
        /// </summary>
        public void SetGoalCooldown(uint goalId, long ticksNow, int expireTicksFrom, int expireTicksTo) {
            goalCooldowns ??= new();
            goalCooldowns[goalId] = ticksNow + Rng.URange(expireTicksFrom, expireTicksTo);
        }

        /// <summary>
        /// Returns true if this agent is cooling down on the given goal and should skip bidding.
        /// Lazily removes the entry once it expires to keep the dict small.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGoalCoolingDown(uint goalId, long ticksNow) {
            if (goalCooldowns == null) { return false; }
            if (goalCooldowns.TryGetValue(goalId, out var afterTick)) {
                if (ticksNow <= afterTick) { return true; }
                // expired — remove so the dict stays tidy
                goalCooldowns.Remove(goalId);
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCoolingDown(int id, long ticksNow) {
            #if !PRODUCTION_BUILD
            if (id == 0) {
                D.Err("Checking cooldown of 0! {0}", agent);
            }
            #endif
            // Fast path: single dictionary lookup, no removal
            if (Cooldowns.TryGetValue(id, out var afterTick)) {
                #if DEBUG_COOLDOWN_CHECK
                var result = ticksNow <= afterTick;
                D.Warn("Checking if cooling down: {0} -> {1} | {2}", id, result, this);
                return result;
                #else
                return ticksNow <= afterTick;
                #endif
            }
            #if DEBUG_COOLDOWN_CHECK
            D.Warn("Checking if cooling down: {0} -> false | {1}", id, this);
            #endif
            return false;
        }

        public bool IsBlacklisted(int i) {
            if (Blacklist == null) { return false; }
            return Blacklist.Contains(i);
        }
        public bool IsBlacklisted2(int i) {
            if (Blacklist2 == null) { return false; }
            return Blacklist2.Contains(i);
        }
        public bool IsBlacklistedUint(uint i) {
            if (BlacklistUint == null) { return false; }
            return BlacklistUint.Contains(i);
        }

        public bool AddToBlacklist(int entityId) {
            if (Blacklist == null) {
                Blacklist = new HashSet<int>();
            }
            return Blacklist.Add(entityId);
        }
        public bool AddToBlacklist2(int i) {
            if (Blacklist2 == null) {
                Blacklist2 = new HashSet<int>();
            }
            return Blacklist2.Add(i);
        }
        public bool AddToBlacklistUint(uint id) {
            if (BlacklistUint == null) {
                BlacklistUint = new HashSet<uint>();
            }
            return BlacklistUint.Add(id);
        }

        public void ClearBlacklist() {
            Blacklist?.Clear();
            Blacklist2?.Clear();
            BlacklistUint?.Clear();
        }

        public void BeforeSave() {
            GoalNeedIds ??= new();
            GoalNeedIds.Clear();
            TargetMatTypeId = TargetMatType?.IdH ?? 0;
            TargetEntityId = TargetEntity?.Id ?? 0;
            TargetTileId = TargetTile?.Id ?? 0;
            TargetBeingId = TargetBeing?.Id ?? 0;
            TargetObjDataId = TargetObjData?.Id ?? 0;
            foreach (var ng in NeedGoals) {
                // D.Warn("Saving need goal: {0} ({1}): {2} | {3}", ng.Key, NeedIdH.ResolveName(ng.Key), ng.Value, this);
                GoalNeedIds[ng.Value.Id] = ng.Key;
            }
        }

        public void SetParticles(ParticlesSys.Data data) {
            Particles = data;
            agent.S.Sys.Particles.AddParticles(data);
        }

        public void ClearParticles() {
            if (Particles != null) {
                agent.S.Sys.Particles.RemoveParticles(Particles);
                Particles = null;
            }
        }

        public void SetChatContext(SocialChatContext ctx) {
            ChatContext = ctx;
        }

        public void ClearChatContext() {
            ChatContext = null;
        }

        /// <summary>
        /// Have to call this after goals are loaded
        /// </summary>
        public void AfterLoad(GameState s, List<AIGoal> goals) {
            if (TargetMatTypeId != 0) {
                TargetMatType = MatType.FastGet(TargetMatTypeId);
                if (TargetMatType == null) {
                    D.Err("Failed deserializing target mat type by id h {0} ({1}) for agent bb: {0}",
                        TargetMatTypeId);
                    TargetMatTypeId = 0;
                }
            }
            if (TargetEntityId != 0) {
                TargetEntity = s.FindEntity<Entity>(TargetEntityId);
                if (TargetEntity == null) {
                    D.Err("Failed deserializing target entity by id for agent bb: {0}",
                        TargetEntityId);
                    TargetEntityId = 0;
                }
            }
            if (TargetTileId != 0) {
                TargetTile = s.FindEntity<Tile>(TargetTileId);
                if (TargetTile == null) {
                    D.Err("Failed deserializing target tile by id for agent bb: {0}",
                        TargetTileId);
                    TargetTileId = 0;
                }
            }
            if (TargetBeingId != 0) {
                TargetBeing = s.Beings.Find(TargetBeingId);
                if (TargetBeing == null) {
                    D.Err("Failed finding target being by id for agent bb: {0}",
                        TargetBeingId);
                    TargetBeingId = 0;
                }
            }
            if (TargetObjDataId != 0) {
                TargetObjData = s.Query.FindObjData.Ask(TargetObjDataId);
                if (TargetObjData == null) {
                    D.Err("Failed finding target objData by id for agent bb: {0}",
                        TargetObjDataId);
                    TargetObjDataId = 0;
                }
            }

            if (Arrays.IsEmpty(GoalNeedIds)) { return; }
            if (NeedGoals.Count > 0) {
                D.Err("Trying to deserialize need goals ids when need goals are not null!");
                return;
            }
            // need to do this to retain the order
            var needGoals = new Dictionary<int, AIGoal>();
            foreach (var g in goals) {
                if (GoalNeedIds.TryGetValue(g.Id, out var needId)) {
                    needGoals[needId] = g;
                }
            }
            foreach (var g in GoalNeedIds) {
                if (needGoals.TryGetValue(g.Value, out var goal)) {
                    if (g.Key == goal.Id) {
                        // D.Warn("Loading need goal: {0} ({1}): {2} | {3}", g.Value, NeedIdH.ResolveName(g.Value), g, this);
                        NeedGoals[g.Value] = goal;
                    } else {
                        D.Err("Goal id mismatch in need goals! {0} != {1} for need goal: {2}", g.Key, goal.Id, goal);
                    }
                }
            }
            if (GoalNeedIds.Count != needGoals.Count) {
                D.Err("Missmatch of saved/loaded need goals. Saved: {0}, Loaded: {1}", GoalNeedIds.Count, NeedGoals.Count);
            }
        }
        public bool TryGetCachedActionCost(int actionId, long tick,
                out float cost) {
            if (actionCostCache.TryGetValue(actionId, out var costTicks)) {
                cost = costTicks.Item1;
                return costTicks.Item2 > tick;
            }
            cost = 0f;
            return false;
        }

        public void SetCachedActionCost(int actionId, float w,
                long tick, int expireInTicks) {
            actionCostCache[actionId] = (w, tick + expireInTicks);
        }

        public bool TryReserveSlotSide(Slot slot, int sideIdx, AIGoal goal) {
            var b = agent.Being;
            if (ReservedSlot != null) {
                D.Err("Trying to reserve a slot while another one is already reserved! New slot: {0}, Existing reservation: {1}, Res goal: {2}, Agent: {3}",
                    slot, ReservedSlot, ReservedSlotGoal, agent);
                ReservedSlot.Unreserve(b);
                if (ReservedSlot.ContainedBeing == b) {
                    ReservedSlot.Remove(b);
                }
                ReservedSlot = null;
                ReservedSlotGoal = null;
            }
            if (slot.ReserveSide(b, sideIdx)) {
                ReservedSlot = slot;
                ReservedSideIdx = sideIdx;
                ReservedSlotGoal = goal;
                return true;
            }
            return false;
        }
        /// <summary>
        /// Sets the slot without, in cases where slot is needed without one
        /// <alsosee cref="ClearReservedSlot"/>
        /// </summary>
        public void SetReservedSlot(Slot slot) {
            ReservedSlot = slot;
        }
        /// <summary>
        /// Clears the slot without clearing the reservation. Reservation should not be set!
        /// <alsosee cref="SetReservedSlot(Slot)"/>
        /// </summary>
        public void ClearReservedSlot() {
            if (ReservedSlot == null) { return; }
            if (ReservedSlot.ReservedBy != agent.Being) {
                if (ReservedSlot.ReservedBy != null) {
                    D.Err("Trying to clear slot reservation while it is reserved by someone else! {0} => {1}. {2}",
                        ReservedSlot.ReservedBy, agent, ReservedSlot.Parent);
                }
                RemoveActiveReservation(agent.CurrentAction);
            } else if (activeReservations.Count > 0) {
                RemoveActiveReservation(agent.CurrentAction);
            }
            ReservedSlot = null;
        }
        public bool TryReserveSlot(Slot slot, AIGoal goal) {
            var b = agent.Being;
            if (ReservedSlot != null) {
                #if !PRODUCTION_BUILD
                D.Err("Trying to reserve a slot while another one is already reserved! New slot: {0}, Existing reservation: {1}, Goal of reservation: {2}, Agent: {3}",
                    slot, ReservedSlot, ReservedSlotGoal, agent);
                #endif
                ReservedSlot.Unreserve(b);
                if (ReservedSlot.ContainedBeing == b) {
                    ReservedSlot.Remove(b);
                }
                ReservedSlotGoal = null;
                ReservedSlot = null;
            }
            if (slot.Reserve(b)) {
                ReservedSlotGoal = goal;
                ReservedSlot = slot;
                ReservedSideIdx = -1;
                return true;
            }
            return false;
        }

        public void UnreserveSlotSide(bool addToIgnoreList) {
            var slot = ReservedSlot;
            if (slot != null) {
                slot.Unreserve(agent.Being, ReservedSideIdx);
                if (addToIgnoreList) {
                    AddToBlacklist(slot.Parent.EntityId);
                }
                ReservedSideIdx = -1;
                ReservedSlot = null;
                ReservedSlotGoal = null;
            }
        }

        public void UnreserveSlot(bool addToIgnoreList, bool keepUsing) {
            var slot = ReservedSlot;
            if (slot == null) { return; }
            if (keepUsing) {
                if (slot.ContainedBeing != agent.Being) {
                    // no point of keeping it reserved
                    slot.Unreserve(agent.Being);
                }
            } else {
                if (slot.ContainedBeing == agent.Being) {
                    slot.Remove(agent.Being);
                }
                slot.Unreserve(agent.Being);
            }
            if (addToIgnoreList) {
                AddToBlacklist(slot.Parent.EntityId);
            }
            ReservedSideIdx = -1;
            ReservedSlot = null;
            ReservedSlotGoal = null;
        }

        public void ClearFauxObj() {
            if (FauxObj != null) {
                FauxObj.DetachAll();
                FauxObj.RemoveFromRenderer(agent.S);
                FauxObj = null;
            }
        }
        public void Validate(GameState s, AIGoal lastGoal, AIAction lastAct) {
            if (ReservedSlot != null) {
                // -1 is a whole-slot reservation (TryReserveSlot), any other index a side
                // (TryReserveSlotSide). The branches were swapped, so a leaked whole-slot
                // reservation was released without slot.Remove(being): the being stayed in
                // the device and the bed or chair stayed occupied.
                if (ReservedSideIdx == -1) {
                    if (agent.IsRegistered) {
                        D.Err("Blackboard has a reserved SLOT after act: {0}. res goal: {1}. last goal: {2}, agent {3}",
                            lastAct, ReservedSlotGoal, lastGoal, agent);
                    }
                    UnreserveSlot(addToIgnoreList: false, keepUsing: false);
                } else {
                    if (agent.IsRegistered) {
                        D.Err("Blackboard has a reserved slot SIDE after act: {0}. res goal: {1}, last goal: {2}. agent {3}",
                            lastAct, ReservedSlotGoal, lastGoal, agent);
                    }
                    UnreserveSlotSide(false);
                }
            }
            if (Blacklist?.Count > 0) {
                if (agent.IsRegistered) {
                    D.Err("Blackboard Blacklist not empty after act: {0}. goal: {1}. agent: {2}",
                        lastAct, lastGoal, agent);
                }
                Blacklist?.Clear();
            }
            if (BlacklistUint?.Count > 0) {
                if (agent.IsRegistered) {
                    D.Err("Blackboard BlacklistUint not empty after act: {0}. goal: {1}. agent: {2}",
                        lastAct, lastGoal, agent);
                }
                BlacklistUint?.Clear();
            }
            if (FauxObj != null) {
                D.Err("Blackboard FauxOBJ not empty after act: {0}. goal: {1}. agent: {2}",
                    lastAct, lastGoal, agent);
                ClearFauxObj();
            }
            if (Opportunity != null) {
                D.Err("Blackboard Opportunity not empty after act: {0}. goal: {1}. agent: {2}",
                    lastAct, lastGoal, agent);
                RemoveOpportunity(s);
            }
            if (Particles != null) {
                D.Err("Blackboard Particles not empty after act: {0}. goal: {1}. agent: {2}",
                    lastAct, lastGoal, agent);
                ClearParticles();
            }
            if (FX != null) {
                D.Err("Blackboard FX not empty after act: {0}. goal: {1}. agent: {2}",
                    lastAct, lastGoal, agent);
                ClearFX();
            }
            if (Notification.IsValid) {
                var notifData = s.Query.GetNotification.Ask(Notification);
                D.Err("Notification is not empty after act: {0}. goal: {1}. agent: {2}. Notification: {3}",
                    lastAct, lastGoal, agent, notifData);
                ClearNotification();
            }
            if (activeReservations.Count > 0) {
                if (agent.IsRegistered) {
                    D.Err("Active reservations ({0}) not empty after {2} | act: {1}. goal: {3}. agent: {4}",
                        activeReservations.Count, lastAct, lastGoal, activeReservations[0], agent);
                }
                RemoveActiveReservation(lastGoal);
            }
            if (ChatContext != null) {
                D.Err("ChatContext is not empty after act: {0}. goal: {1}. agent: {2}",
                    lastAct, lastGoal, agent);
                ClearChatContext();
            }
            if (agent.IsRegistered) {
                if (HasRequestedReachability) {
                    D.Err("Has requested reachability not cleared after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    HasRequestedReachability = false;
                }
                if (IsTargetReachable) {
                    D.Err("Is target reachable not cleared after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    IsTargetReachable = false;
                }
                if (IsWaitingForPathEstimate) {
                    D.Err("Waiting for path estimate not cleared after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    IsWaitingForPathEstimate = false;
                }
                if (WaitingForEstimateTicks != 0) {
                    D.Err("Waiting for path estimate ticks not cleared after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    WaitingForEstimateTicks = 0;
                }
                if (agent.Being.Graphics.IsCarrying()) {
                    D.Err("Carrying something after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                }
                if (TargetBeing != null) {
                    D.Err("Target Being not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    TargetBeing = null;
                }
                if (TargetEntity != null) {
                    D.Err("Target Entity not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    TargetEntity = null;
                }
                if (TargetTile != null) {
                    D.Err("Target Tile not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    TargetTile = null;
                }
                if (TargetMatType != null) {
                    D.Err("Target MatType not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    TargetMatType = null;
                }
                if (TargetObjData != null) {
                    D.Err("Target ObjData not empty after act: {0}. goal: {1}. Target obj: {2}",
                        lastAct, lastGoal, TargetObjData);
                    TargetObjData = null;
                }
                if (Object1 != null) {
                    D.Err("Object1 not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    Object1 = null;
                }
                if (Object2 != null) {
                    D.Err("Object2 not empty after act: {0}. goal: {1}",
                        lastAct, lastGoal);
                    Object2 = null;
                }
                if (MoveProgress != null) {
                    D.Err("MoveProgress not empty after act: {0}. goal: {1}.",
                        lastAct, lastGoal);
                    MoveProgress = null;
                    MoveTargetPos = 0;
                }
            }
        }

        public void AddOpportunity(GameState s, SocialOpportunity op) {
            if (Opportunity != null) {
                D.Err("Trying to add a new opportunity while one already exists! {0}. Existing: {1}",
                    op, Opportunity);
                return;
            }
            if (s.Query.ChangeSocialOpportunity.Ask(op, true)) {
                Opportunity = op;
            }
        }

        public void RemoveOpportunity(GameState s) {
            if (Opportunity != null) {
                if (!Opportunity.IsExpired) {
                    s.Query.ChangeSocialOpportunity.Ask(Opportunity, false);
                }
                Opportunity = null;
            }
        }

        public bool RemoveActiveReservation(object ctx) {
            if (activeReservations.Count == 0) {
                return false;
            }
            // Cancel all reservations
            for (int i = activeReservations.Count - 1; i >= 0; i--) {
                var res = activeReservations[i];
                if (res.Storage.Entity.IsActive) {
                    res.Storage.CancelReservationFor(res.Being, res.Type, ctx);
                }
            }
            activeReservations.Clear();
            return true;
        }

        public void ShowNotification(NotificationData data) {
            if (Notification.IsValid) {
                D.Err("Trying to show more than one notification! Existing: {0}. New: {1}",
                    Notification, data);
            }
            data.Being = agent.BeingData;
            Notification = A.S.Query.AddNotification.Ask(data);
        }

        public void ClearNotification() {
            if (Notification.IsValid) {
                Notify.Notification.Remove(ref Notification);
                Notification = default;
            }
        }

        public void CreateFauxObj(SpriteData sd) {
            var worker = agent.Being;
            FauxObj = new AnimatedSpriteSet(this, 1, WorldLayer.FreeEntities,
                worker.Graphics.Position + worker.Graphics.CarryOffset(out var _));
            FauxObj.SetSprite(0, sd, worker.S);
            FauxObj.SetSortOrder(worker.S, 0, SpriteOrder.Obj);
            FauxObj.AttachTo(worker.Graphics.SpriteSet);
            FauxObj.AddToRenderer(worker.S);
            agent.BB.FauxObj = FauxObj;
        }

        public void ClearMoveTarget() {
            MoveTargetPos = 0;
            MoveProgress = null;
        }
    }
}
