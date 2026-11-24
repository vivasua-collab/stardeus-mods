#if !PRODUCTION_BUILD
// #define DEBUG_AI_DELIVERY_MULTI
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Creatures;
using Game.Systems.Inventory;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionGatherMatsMulti : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionGatherMatsMulti());
        }
        public const string ActId = "GatherMatsMulti";
        private AIActionGatherMatsMulti() : base(ActId, () => T.ActGatherrawmaterials) {
            Preconditions = AIState.With(AIVarsH.IsResourcesDelivered, false);
            Outcomes = AIState.With(AIVarsH.IsResourcesDelivered, true);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public enum Phase {
            CheckReachability,
            EvaluateRequests,
            FindNextStorage,
            NavigateToStorage,
            CollectFromStorage,
            BringMats,
            DeliverMats,
            DeliverToStops,
        }

        public sealed class Memory : ISaveable {
            // Saveable
            public Phase Phase;
            public int ProvideMatAttempts;
            public List<int> CarriedMatObjIds;
            // Temporal
            public int Estimations;
            public IMatRequester MatRequester;
            public List<MatRequest> RemainingRequests;
            public List<UnstoredMatComp> CarriedMats;
            public IMatStorage SeekedStorage;
            public int SeekedStoragePos = -1;
            public HashSet<IMatStorage> FailedStorages;
            public Dictionary<MatType, int> ReservedAmts;
            // Walked up to SeekedStorage a second time because the first walk ended out of reach
            public bool RetriedApproach;
            // Set once anything was actually handed over, so a run that emptied the
            // colony's stock into a still-unfinished request ends as a success instead
            // of a failure with a growing retry backoff
            public bool HasDelivered;
            // Productive deliveries this run that still left the requester wanting
            public int TripsDelivered;
            // Other blueprints supplied on this trip. Not saved: their holds end with the
            // action, and a trip resumed after a load forms its batch again
            public ConstructionDeliveryBatch Batch;
            public bool IsBatchFormed;

            public void Load(ComponentData data) {
                ProvideMatAttempts = data.GetInt("Attempts", 0);
                var idStr = data.GetString("CarriedIds", null);
                if (idStr != null) {
                    var parts = idStr.Split(',');
                    CarriedMatObjIds = new List<int>(parts.Length);
                    for (int i = 0; i < parts.Length; i++) {
                        if (int.TryParse(parts[i], out var id)) {
                            CarriedMatObjIds.Add(id);
                        }
                    }
                }
            }

            public ComponentData Save() {
                var data = new ComponentData(0, null);
                data.SetInt("Attempts", ProvideMatAttempts);
                if (CarriedMats != null && CarriedMats.Count > 0) {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < CarriedMats.Count; i++) {
                        if (i > 0) { sb.Append(','); }
                        sb.Append(CarriedMats[i].EntityId);
                    }
                    data.SetString("CarriedIds", sb.ToString());
                }
                return data;
            }

            public static bool For(AIAgentComp agent, AIGoal goal) {
                var mem = new Memory();
                #if DEBUG_AI_DELIVERY_MULTI
                D.Warn("Creating memory for multi delivery action for: {0} => {1}",
                    goal, agent);
                #endif
                if (agent.ActionMemoryData != null) {
                    mem.Load(agent.ActionMemoryData);
                    agent.ActionMemoryData = null;
                }
                if (goal.Target is IMatRequester mreq) {
                    mem.MatRequester = mreq;
                } else {
                    D.Err("Goal target is not a IMatRequester: {0} -> {1}",
                        goal.Target, goal);
                    return false;
                }
                mem.CarriedMats ??= new();
                mem.ReservedAmts ??= new();
                agent.ActionMemory = mem;
                mem.RestoreCarriedMats(agent);
                return true;
            }

            // Carrying is not saved, so the load comes back as loose piles on the
            // worker's tile. Pick them up again before the action resumes; otherwise
            // they are left behind or collected again as if they were a storage.
            // This runs on the first Execute after loading, not in AfterLoad: the memory
            // does not exist yet while the agent is being deserialized.
            private void RestoreCarriedMats(AIAgentComp agent) {
                if (CarriedMatObjIds == null) { return; }
                var ids = CarriedMatObjIds;
                CarriedMatObjIds = null;
                var worker = agent.Being;
                if (worker.Graphics.IsCarrying()) { return; }
                var carryCtrl = worker.Graphics.CarryCtrl;
                for (int i = 0; i < ids.Count; i++) {
                    var pile = agent.S.Components.Find<UnstoredMatComp>(ids[i]);
                    if (pile == null || !pile.Entity.IsActive || pile.StackSize < 1) { continue; }
                    // Someone else claimed it in the meantime
                    if (pile.Entity.IsCarriedByAny || pile.ReservationCount > 0) { continue; }
                    if (Pos.SqrDistance(pile.Entity.PosIdx, worker.PosIdx) > 2) { continue; }
                    // Loading put it back into the inventory, carried mats stay out of it
                    if (pile.IsAddedToInventory) {
                        pile.RemoveFromInventory();
                    }
                    var carried = CarriedMats.Count == 0
                        ? worker.Graphics.Carry(pile.Obj, true, true)
                        : carryCtrl != null && carryCtrl.CarryAdditional(pile.Obj);
                    if (carried) {
                        CarriedMats.Add(pile);
                    } else {
                        pile.ReaddToInventoryIfNeeded();
                    }
                }
            }

            public void Cleanup(AIAgentComp agent) {
                agent.Motion.OnPathEstimate = null;
                Batch?.ReleaseAll();
                Batch = null;
                // Drop all additional carried items first
                var carryCtrl = agent.Being.Graphics?.CarryCtrl;
                carryCtrl?.DropAllAdditional();
                // Then drop primary
                if (CarriedMats != null) {
                    for (int i = 0; i < CarriedMats.Count; i++) {
                        var mat = CarriedMats[i];
                        mat?.ReaddToInventoryIfNeeded();
                    }
                    CarriedMats.Clear();
                }
                agent.Being.Graphics?.DropIfCarrying();
                // Cancel any remaining storage reservations
                if (ReservedAmts != null && ReservedAmts.Count > 0) {
                    SeekedStorage?.CancelReservationFor(agent.Being, null);
                    ReservedAmts.Clear();
                }
            }

            public int TotalMissingAmt {
                get {
                    if (RemainingRequests == null) { return 0; }
                    int total = 0;
                    for (int i = 0; i < RemainingRequests.Count; i++) {
                        var req = RemainingRequests[i];
                        if (req.IsCancelled) { continue; }
                        // Subtract already carried
                        var carried = FindCarriedStackSize(req.Type);
                        var remaining = Maths.Max(0, req.Amount - carried);
                        total += remaining;
                    }
                    return total;
                }
            }

            public int FindCarriedStackSize(MatType type) {
                if (CarriedMats == null) { return 0; }
                for (int i = 0; i < CarriedMats.Count; i++) {
                    if (CarriedMats[i]?.Type == type) {
                        return CarriedMats[i].StackSize;
                    }
                }
                return 0;
            }

            public bool AnyRequestsRemaining {
                get {
                    if (RemainingRequests == null) { return false; }
                    for (int i = 0; i < RemainingRequests.Count; i++) {
                        var req = RemainingRequests[i];
                        if (req.IsCancelled) { continue; }
                        if (StillToGather(req) > 0) { return true; }
                    }
                    return false;
                }
            }

            // The requester's own need plus what the other blueprints of the trip need,
            // minus what is already carried
            public int StillToGather(MatRequest req) {
                var wanted = req.Amount;
                if (Batch != null) {
                    wanted += Batch.ExtraFor(req.Type);
                }
                return wanted - FindCarriedStackSize(req.Type);
            }
        }

        private AIActionResult SuccessFor(AIGoal goal) {
            goal.WithWorld(AIVarsH.IsResourcesDelivered, true);
            return Success;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (!TryGetMemory(agent, goal, out var mem)) {
                return Error("Failed getting AI task memory for ActionGatherMatsMulti");
            }

            var phase = mem.Phase;

            if (agent.BB.MoveProgress != null) {
                if (agent.BB.MoveProgress.IsMoving) {
                    if ((phase != Phase.CollectFromStorage && phase != Phase.NavigateToStorage)
                            || !InventorySys.IsForbiddenFor(mem.SeekedStorage, agent.Being)) {
                        return InProgress;
                    }
                    // Forbidden while we were on the way: turn back and look for another
                    // source. It is not out of reach, so no nav failure or flyer mark
                    agent.Motion.Abort(T.AdjForbidden);
                    mem.SeekedStorage.CancelReservationFor(agent.Being, null);
                    mem.ReservedAmts.Clear();
                    mem.SeekedStorage = null;
                    mem.Phase = phase = Phase.FindNextStorage;
                } else if (agent.BB.MoveProgress.HasFailed) {
                    if (phase == Phase.CollectFromStorage
                            || phase == Phase.NavigateToStorage) {
                        agent.BB.MoveProgress = null;
                        var lastStoragePos = mem.SeekedStoragePos;
                        if (!IgnoreAndKeepSearching(agent, mem)) {
                            if (agent.Motion.IsGrounded) {
                                return FailedSoft(T.AdRejUnreachableMatGrounded);
                            }
                            return FailedNavToMat(agent, lastStoragePos);
                        }
                        mem.Phase = phase = Phase.FindNextStorage;
                    } else if (phase == Phase.DeliverToStops) {
                        // only that stop is given up, the trip goes on
                        mem.Batch?.OnWalkFailed();
                    } else {
                        if (agent.BB.MoveTargetPos > 0) {
                            return FailedNavToTargetWithoutStore(agent);
                        } else {
                            return Failed(T.AdRejTargetUnavailable);
                        }
                    }
                }
                agent.BB.MoveProgress = null;
                agent.BB.MoveTargetPos = Pos.Invalid;
            }

            switch (phase) {
                case Phase.CheckReachability: return CheckReachability(goal, agent, mem);
                case Phase.EvaluateRequests: return EvaluateRequests(goal, agent, mem);
                case Phase.FindNextStorage: return FindNextStorage(goal, agent, mem);
                case Phase.NavigateToStorage: return NavigateToStorage(goal, agent, mem);
                case Phase.CollectFromStorage: return CollectFromStorage(goal, agent, mem);
                case Phase.BringMats: return BringMats(goal, agent, mem);
                case Phase.DeliverMats: return DeliverMats(goal, agent, mem);
                case Phase.DeliverToStops: return DeliverToStops(agent, mem);
            }

            D.Err("Unhandled phase: {0}", phase);
            return Error("Unhandled GatherMatsMulti phase: {0}", phase);
        }

        private AIActionResult EvaluateRequests(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            var missingMats = mem.MatRequester.MissingMats;
            if (missingMats == null || missingMats.Count == 0) {
                return SuccessFor(goal);
            }
            // Snapshot the remaining requests
            mem.RemainingRequests = new List<MatRequest>(missingMats.Count);
            for (int i = 0; i < missingMats.Count; i++) {
                var req = missingMats[i];
                if (req.IsCancelled || req.Amount < 1) { continue; }
                mem.RemainingRequests.Add(req);
            }
            if (mem.RemainingRequests.Count == 0) {
                return SuccessFor(goal);
            }
            if (!mem.IsBatchFormed) {
                mem.IsBatchFormed = true;
                int carried = 0;
                for (int i = 0; i < mem.CarriedMats.Count; i++) {
                    carried += mem.CarriedMats[i]?.StackSize ?? 0;
                }
                mem.Batch = ConstructionDeliveryBatch.TryForm(agent, goal,
                    mem.MatRequester, carried);
            }
            mem.Phase = Phase.FindNextStorage;
            return InProgress;
        }

        private AIActionResult FindNextStorage(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            var maxCarry = agent.SpareCarryAmount;
            if (maxCarry < 1) {
                if (mem.CarriedMats.Count > 0) {
                    mem.Phase = Phase.BringMats;
                    return InProgress;
                }
                return Failed(T.AdRejInterrupted);
            }

            if (!mem.AnyRequestsRemaining) {
                if (mem.CarriedMats.Count > 0) {
                    mem.Phase = Phase.BringMats;
                    return InProgress;
                }
                return SuccessFor(goal);
            }

            // Find the closest storage that has ANY of the needed materials
            var worker = agent.Being;
            var inventory = worker.S.Sys.Inventory;

            // Use the callback-based overload to check for any needed mat type.
            // Only material nobody stronger is waiting on counts: the doability gate
            // ran once when the goal started, and a long trip loop would otherwise keep
            // taking every unit produced from under a higher priority claim
            // (see MaterialClaims.md). The claim check runs last, only for storages
            // that actually hold the material.
            var requester = mem.MatRequester;
            var priority = goal.GoalPriority;
            IMatStorage bestStorage = inventory.FindClosestReachableStorage(
                worker, mem.FailedStorages,
                (storage) => {
                    for (int i = 0; i < mem.RemainingRequests.Count; i++) {
                        var req = mem.RemainingRequests[i];
                        if (req.IsCancelled) { continue; }
                        var stillNeeded = mem.StillToGather(req);
                        if (stillNeeded > 0 && storage.Available(req.Type) > 0
                                && inventory.AvailableFor(req.Type, requester,
                                    priority) > 0) {
                            return true;
                        }
                    }
                    return false;
                }, forTaking: true);

            if (bestStorage == null) {
                // No storage has any needed materials
                if (mem.CarriedMats.Count > 0) {
                    // Bring what we have
                    mem.Phase = Phase.BringMats;
                    return InProgress;
                }
                if (mem.HasDelivered) {
                    // Everything the colony had is already in the requester. Failing
                    // here would pile a growing retry backoff onto a task that did make
                    // progress, and the next partial batch has to be picked up promptly
                    // (see MaterialClaims.md) - so end the trip as done instead.
                    return SuccessFor(goal);
                }
                // Find the first remaining request to report as lacking
                for (int i = 0; i < mem.RemainingRequests.Count; i++) {
                    var req = mem.RemainingRequests[i];
                    if (req.IsCancelled) { continue; }
                    var carried = mem.FindCarriedStackSize(req.Type);
                    if (req.Amount - carried > 0) {
                        if (inventory.AvailableFor(req.Type, requester, priority) < 1
                                && inventory.AllowedCount(req.Type) > 0) {
                            // The material exists, it is just earmarked elsewhere
                            return FailedMatReserved();
                        }
                        worker.Motion.StoreNavFailureAt(goal.Target.Entity.PosIdx);
                        if (inventory.AllowedCount(req.Type) > 0) {
                            // Stock exists but is out of this worker's reach - not
                            // a shortage (see AIActionGatherMats.SearchForMat)
                            inventory.MarkUnreachableByFlying(req.Type, worker);
                            return FailedUnreachableMat(agent, req.Type);
                        }
                        return FailWithNotifyLackOf(worker.S, req.Type,
                            req.Amount, mem.MatRequester);
                    }
                }
                return Failed(T.AdRejLackingMat);
            }

            // Reserve what we can from this storage across all needed types
            mem.SeekedStorage = bestStorage;
            mem.SeekedStoragePos = bestStorage.OutPort;
            mem.RetriedApproach = false;
            mem.ReservedAmts.Clear();

            for (int i = 0; i < mem.RemainingRequests.Count; i++) {
                var req = mem.RemainingRequests[i];
                if (req.IsCancelled) { continue; }
                var stillNeeded = mem.StillToGather(req);
                if (stillNeeded <= 0) { continue; }
                var available = bestStorage.Available(req.Type);
                if (available <= 0) { continue; }
                var toReserve = Maths.Min(stillNeeded, maxCarry);
                if (toReserve < 1) { continue; }
                var reserved = bestStorage.Reserve(req.Type, worker, toReserve, null);
                if (reserved > 0) {
                    mem.ReservedAmts[req.Type] = reserved;
                    maxCarry -= reserved;
                    if (maxCarry < 1) { break; }
                }
            }

            if (mem.ReservedAmts.Count == 0) {
                // Could not reserve anything
                if (!IgnoreAndKeepSearching(agent, mem)) {
                    return FailedUnreachableStorage(agent, goal.Target.Entity.PosIdx);
                }
                mem.Phase = Phase.FindNextStorage;
                return InProgress;
            }

            // Navigate to the storage
            if (bestStorage.OutPort != worker.PosIdx) {
                if (!TryGoToStorage(agent, mem)) {
                    if (!IgnoreAndKeepSearching(agent, mem)) {
                        return FailedUnreachableStorage(agent, agent.BB.MoveTargetPos);
                    }
                    mem.Phase = Phase.FindNextStorage;
                    return InProgress;
                }
            }

            mem.Phase = Phase.CollectFromStorage;
            return InProgress;
        }

        private AIActionResult NavigateToStorage(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            // This phase is reached after movement completes, transition to collect
            mem.Phase = Phase.CollectFromStorage;
            return InProgress;
        }

        private AIActionResult CollectFromStorage(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            if (mem.SeekedStorage == null || !mem.SeekedStorage.Entity.IsActive) {
                mem.SeekedStorage = null;
                mem.ReservedAmts.Clear();
                mem.Phase = Phase.FindNextStorage;
                return InProgress;
            }

            var worker = agent.Being;
            var seekedStorage = mem.SeekedStorage;
            var carryCtrl = worker.Graphics.CarryCtrl;

            // A pile gives nothing to a worker more than a step away, and taking anyway wasted
            // the trip. Walk up to it once more, then look elsewhere
            if (seekedStorage is UnstoredMatComp seekedPile && !seekedPile.IsInTakeRange(worker)) {
                if (!mem.RetriedApproach && TryGoToStorage(agent, mem)) {
                    mem.RetriedApproach = true;
                    return InProgress;
                }
                if (!IgnoreAndKeepSearching(agent, mem)) {
                    return FailedUnreachableStorage(agent, agent.BB.MoveTargetPos);
                }
                mem.Phase = Phase.FindNextStorage;
                return InProgress;
            }

            // Take each reserved material type from the storage
            foreach (var kv in mem.ReservedAmts) {
                var matType = kv.Key;
                var reservedAmt = kv.Value;
                if (reservedAmt < 1) { continue; }

                var takenAmt = seekedStorage.Take(matType, worker, reservedAmt, null);
                if (takenAmt < 1) { continue; }

                // Check if we already carry this mat type — grow its stack
                var existingCarried = carryCtrl.FindCarriedMat(matType);
                if (existingCarried != null) {
                    existingCarried.StackSize += takenAmt;
                    existingCarried.MaxStackSize = existingCarried.StackSize;
                } else {
                    // Spawn a new carried pile
                    var pile = EntityUtils.SpawnRawMaterial(new Mat() {
                        StackSize = takenAmt,
                        MaxStackSize = takenAmt,
                        Type = matType,
                    }, worker.PosIdx, 0, true, false);
                    if (pile == null || pile.StackSize < 1) {
                        D.Err("Failed spawning carried mat pile in GatherMatsMulti: {0}",
                            matType);
                        continue;
                    }
                    pile.RemoveFromInventory();

                    if (mem.CarriedMats.Count == 0) {
                        // First item — use primary carry
                        if (!worker.Graphics.Carry(pile.Obj, false, false)) {
                            D.Err("Failed carrying first mat {0} in GatherMatsMulti",
                                pile);
                            pile.ReaddToInventoryIfNeeded();
                            continue;
                        }
                    } else {
                        // Additional items — use CarryController
                        if (!carryCtrl.CarryAdditional(pile.Obj)) {
                            D.Err("Failed carrying additional mat {0} in GatherMatsMulti",
                                pile);
                            pile.ReaddToInventoryIfNeeded();
                            continue;
                        }
                    }
                    mem.CarriedMats.Add(pile);
                }
            }
            mem.ReservedAmts.Clear();
            agent.BB.RemoveActiveReservation(this);

            // Decide: get more or bring what we have
            if (agent.SpareCarryAmount > 0 && mem.AnyRequestsRemaining) {
                mem.Phase = Phase.FindNextStorage;
            } else {
                mem.Phase = Phase.BringMats;
            }
            return InProgress;
        }

        private AIActionResult BringMats(AIGoal goal, AIAgentComp agent, Memory mem) {
            if (mem.CarriedMats == null || mem.CarriedMats.Count == 0) {
                D.Err("BringMats with no carried mats!");
                mem.Phase = Phase.EvaluateRequests;
                return InProgress;
            }
            // The other blueprints of the trip come first, the task's own one last
            if (mem.Batch != null && mem.Batch.HasStops) {
                mem.Phase = Phase.DeliverToStops;
                return InProgress;
            }

            var matRequester = mem.MatRequester;
            var worker = agent.Being;
            // Navigate to target — same logic as AIActionGatherMats.BringCollectedMat
            if (matRequester.InPort == 0) {
                if (matRequester.Entity is Tile t) {
                    t = EntityUtils.SelfOrBlockingObjAbove(t);
                    bool went;
                    int pos;
                    if (t.Transform.HasInPort) {
                        pos = t.Transform.InPort;
                        went = agent.Being.Motion.TryGoTo(pos,
                            PathReqFlags.AdjOnTop | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress);
                    } else {
                        pos = t.PosIdx;
                        went = agent.Being.Motion.TryGoTo(t,
                            PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress);
                    }
                    agent.BB.MoveTargetPos = pos;
                    if (!went) {
                        FailedNavToTarget(agent, t.PosIdx);
                    }
                } else {
                    if (matRequester.Entity != null) {
                        D.Err("IMatRequester wants mat near itself, but it's not a Tile: {0}",
                            matRequester.Entity);
                    }
                    return Failed(T.AdRejTargetUnavailable);
                }
            } else {
                if (worker.PosIdx != matRequester.InPort) {
                    if (agent.Motion.TryGoTo(matRequester.InPort,
                            PathReqFlags.AdjOnTop | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress)) {
                        agent.BB.MoveTargetPos = matRequester.InPort;
                    } else {
                        FailedNavToTarget(agent, matRequester.InPort);
                    }
                }
            }

            mem.Phase = Phase.DeliverMats;
            return InProgress;
        }

        private AIActionResult DeliverMats(AIGoal goal, AIAgentComp agent, Memory mem) {
            int step = 0;
            try {
                if (!CheckMatRequesterReachable(goal, agent, mem,
                        out bool targetUnavailable, out int targetPosIdx)) {
                    if (targetUnavailable) {
                        return Failed(T.AdRejTargetUnavailable);
                    }
                    return FailedNavToTarget(agent, targetPosIdx);
                }
                step = 1;

                if (mem.CarriedMats == null || mem.CarriedMats.Count == 0) {
                    D.Err("DeliverMats with no carried mats!");
                    mem.Phase = Phase.EvaluateRequests;
                    return InProgress;
                }
                step = 2;

                var matRequester = mem.MatRequester;
                if (matRequester == null || matRequester.Entity == null
                        || !matRequester.Entity.IsActive) {
                    D.ErrSoft("Lost MatRequester, failing GatherMatsMulti: {0}-> {1}",
                        matRequester, goal);
                    return Failed(T.AdRejTargetUnavailable);
                }
                step = 3;

                // Drop all carried items
                var carryCtrl = agent.Being.Graphics?.CarryCtrl;
                carryCtrl?.DropAllAdditional(skipInventoryCheck: true);
                if (agent.Being.Graphics?.Carrying != null) {
                    agent.Being.Graphics.Drop(agent.Being.Graphics.Carrying, true);
                }
                step = 4;

                // Play VFX for total amount
                int totalAmt = 0;
                for (int i = 0; i < mem.CarriedMats.Count; i++) {
                    totalAmt += mem.CarriedMats[i]?.StackSize ?? 0;
                }
                if (totalAmt > 0 && matRequester.Entity != null) {
                    Mat.PlayProvideVFX(agent.S, agent.Being.Position,
                        matRequester.Entity.Position, totalAmt);
                }
                step = 5;

                // Provide all materials at once
                var isFulfilled = matRequester.ProvideMulti(mem.CarriedMats, agent.Being);
                // What the requester actually accepted: its Take shrinks the carried
                // piles by exactly that much
                int leftover = 0;
                for (int i = 0; i < mem.CarriedMats.Count; i++) {
                    leftover += mem.CarriedMats[i]?.StackSize ?? 0;
                }
                var took = totalAmt - leftover;
                if (took > 0) {
                    mem.HasDelivered = true;
                }
                step = 6;

                // Re-add any leftovers to inventory
                for (int i = 0; i < mem.CarriedMats.Count; i++) {
                    mem.CarriedMats[i]?.ReaddToInventoryIfNeeded();
                }
                step = 7;

                mem.CarriedMats.Clear();

                if (isFulfilled) {
                    if (agent.Motion.IsFlying
                            && !agent.Jobs.CanDo(JobTypeIdH.Construction)) {
                        TryStreamline(agent, goal);
                    }
                    return SuccessFor(goal);
                } else if (took < 1) {
                    // The requester refused what we brought - that is the loop this
                    // cap exists for
                    if (mem.ProvideMatAttempts++ > 20) {
                        D.Err("Tried providing materials 20x in GatherMatsMulti: {0}",
                            matRequester.Entity);
                        return Failed(T.OperationFailed);
                    }
                } else {
                    // Delivered, and the requester still wants more. A productive run is
                    // not a stuck one, so it must not count towards the error above - but
                    // one worker should not ferry forever either. Hand the task back to
                    // the auction after a while; the requester re-creates it if still
                    // short. See the same branch in AIActionGatherMats.
                    mem.ProvideMatAttempts = 0;
                    if (++mem.TripsDelivered >= 20) {
                        return SuccessFor(goal);
                    }
                }
                step = 8;

                // Not fully fulfilled, loop back
                mem.Phase = Phase.EvaluateRequests;
                return InProgress;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed delivering gathered materials (multi). STEP: {0} | {1}",
                    step, agent);
                return Failed(T.AdRejInterrupted);
            }
        }

        private AIActionResult DeliverToStops(AIAgentComp agent, Memory mem) {
            if (mem.Batch == null) {
                mem.Phase = Phase.BringMats;
                return InProgress;
            }
            var result = mem.Batch.Step(agent, mem.CarriedMats);
            if (result == ConstructionDeliveryBatch.StepResult.Done) {
                mem.Batch.ReleaseAll();
                mem.Phase = mem.CarriedMats.Count > 0
                    ? Phase.BringMats : Phase.EvaluateRequests;
            }
            return InProgress;
        }

        private bool CheckMatRequesterReachable(AIGoal goal, AIAgentComp agent,
                Memory mem, out bool targetUnavailable, out int targetPosIdx) {
            var matRequester = mem.MatRequester;
            targetUnavailable = false;
            if (matRequester == null) {
                D.Err("Mat requester null in GatherMatsMulti! {1} -> {0}", goal, goal.Target);
                targetUnavailable = true;
                targetPosIdx = Pos.Invalid;
                return false;
            }
            if (!matRequester.Entity.IsActive) {
                targetUnavailable = true;
                targetPosIdx = Pos.Invalid;
                return false;
            }
            if (matRequester.InPort != 0) {
                targetPosIdx = matRequester.InPort;
                if (Pos.SqrDistance(matRequester.InPort, agent.PosIdx) > 2) {
                    return false;
                }
            } else {
                if (matRequester.Entity is Tile t) {
                    t = EntityUtils.SelfOrBlockingObjAbove(t);
                    targetPosIdx = t.PosIdx;
                    if (!EntityUtils.IsNearObj(agent.PosIdx, t)) {
                        return false;
                    }
                } else if (matRequester.Entity is Being b) {
                    targetPosIdx = b.PosIdx;
                    if (Vectors.SqrDistance(b.Position, agent.Motion.Position.Exact) > 2) {
                        return false;
                    }
                } else {
                    targetPosIdx = Pos.Invalid;
                    targetUnavailable = true;
                    return false;
                }
            }
            return true;
        }

        private AIActionResult CheckReachability(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            if (!Ready.Pathfinding) { return InProgress; }
            var worker = agent.Being;
            if (worker.Motion.IsBusy) {
                return InProgress;
            }
            if (!agent.BB.HasRequestedReachability) {
                agent.BB.IsTargetReachable = true;
                var matReq = mem.MatRequester;
                if (!matReq.Entity.IsActive) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (matReq.InPort == 0) {
                    if (matReq.Entity is Tile t) {
                        if (t.PosIdx == 0) {
                            return Failed(T.AdRejTargetUnavailable);
                        }
                        EstimatePathTo(agent, mem, t);
                    } else {
                        if (matReq.Entity == worker) {
                            // clothing task etc
                        } else {
                            EstimatePathTo(agent, mem, matReq.Entity.PosIdx);
                        }
                    }
                } else {
                    EstimatePathTo(agent, mem, matReq.InPort);
                }
            }
            if (agent.BB.IsWaitingForPathEstimate) {
                if (agent.BB.WaitingForEstimateTicks++ > Consts.TicksPer2Hours) {
                    return FailedNavToTarget(agent);
                }
                return InProgress;
            }
            if (agent.BB.IsTargetReachable) {
                mem.Phase = Phase.EvaluateRequests;
                return InProgress;
            }
            return FailedNavToTarget(agent, false);
        }

        private void EstimatePathTo(AIAgentComp agent, Memory mem, Tile t) {
            mem.Estimations++;
            agent.BB.HasRequestedReachability = true;
            agent.BB.MoveTargetPos = t.PosIdx;
            if (agent.BB.IsWaitingForPathEstimate) {
                return;
            }
            if (EntityUtils.IsNearObj(agent.Motion.Position.Idx, t)) {
                agent.BB.IsTargetReachable = true;
                agent.BB.IsWaitingForPathEstimate = false;
                return;
            }
            if (agent.Motion.HadNavFailureAt(t.PosIdx)) {
                agent.BB.IsTargetReachable = false;
                agent.BB.IsWaitingForPathEstimate = false;
                return;
            }
            agent.BB.IsWaitingForPathEstimate = true;
            if (!agent.Motion.EstimatePathTo(t,
                    (res, _) => OnPathEstimated(res, agent))) {
                agent.BB.IsTargetReachable = false;
                agent.BB.IsWaitingForPathEstimate = false;
            }
        }

        private void EstimatePathTo(AIAgentComp agent, Memory mem, int pos) {
            if (pos == 0) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = false;
                return;
            }
            mem.Estimations++;
            agent.BB.HasRequestedReachability = true;
            if (pos == agent.PosIdx) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = true;
                return;
            }
            if (agent.BB.IsWaitingForPathEstimate) {
                return;
            }
            if (agent.Motion.HadNavFailureAt(pos)) {
                agent.BB.IsTargetReachable = false;
                agent.BB.IsWaitingForPathEstimate = false;
                return;
            }
            agent.BB.MoveTargetPos = pos;
            agent.BB.IsWaitingForPathEstimate = true;
            if (!agent.Motion.EstimatePathTo(pos,
                    (res, _) => OnPathEstimated(res, agent))) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = false;
            }
        }

        private void OnPathEstimated(PathRes res, AIAgentComp agent) {
            if (agent.BB.IsWaitingForPathEstimate) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = res.IsValid && res.IsSafe;
                if (!agent.BB.IsTargetReachable) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                }
            }
        }

        private static bool TryGoToStorage(AIAgentComp agent, Memory mem) {
            agent.BB.MoveTargetPos = mem.SeekedStoragePos;
            var flags = PathReqFlags.AdjOnTop | PathReqFlags.TempDestination;
            if (mem.SeekedStorage is UnstoredMatComp pile) {
                // might be harder to reach, let's add nearby too
                flags |= PathReqFlags.AdjNearby;
            }
            bool went = agent.Motion.TryGoTo(mem.SeekedStorage.OutPort,
                flags, out agent.BB.MoveProgress);
            return went;
        }

        private bool IgnoreAndKeepSearching(AIAgentComp agent, Memory mem) {
            var seekedStorage = mem.SeekedStorage;
            if (seekedStorage == null) { return true; }
            mem.FailedStorages ??= new();
            mem.FailedStorages.Add(seekedStorage);
            var worker = agent.Being;
            worker.Motion.StoreNavFailureAt(seekedStorage.OutPort);
            // Cancel reservations for this storage
            seekedStorage.CancelReservationFor(agent.Being, null);
            if (seekedStorage is UnstoredMatComp pile) {
                pile.Obj.Transform.UnstuckIfNeeded();
            }
            if (!worker.Motion.IsGrounded) {
                seekedStorage.MarkAsUnreachableByFlying(worker);
            }
            mem.ReservedAmts.Clear();
            mem.SeekedStorage = null;
            return mem.FailedStorages.Count <= 256;
        }

        private bool TryGetMemory(AIAgentComp agent, AIGoal goal, out Memory memory) {
            if (agent.ActionMemory == null) {
                if (!Memory.For(agent, goal)) {
                    memory = null;
                    return false;
                }
            }
            if (agent.ActionMemory is Memory mem) {
                memory = mem;
                return true;
            } else {
                memory = null;
                D.Err("Wrong or missing memory on the AIAgentComp (multi): {0} -> {1}",
                    agent, agent.ActionMemory);
                return false;
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Only handle multi-material requests
            if (goal.Target is IMatRequester req && !req.IsMulti) {
                return 0f;
            }
            return 1.5f * agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.IsTargetReachable = false;
            agent.BB.HasRequestedReachability = false;
            agent.BB.WaitingForEstimateTicks = 0;
            agent.BB.IsWaitingForPathEstimate = false;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.IsTargetReachable = false;
            agent.BB.HasRequestedReachability = false;
            agent.BB.WaitingForEstimateTicks = 0;
            agent.BB.IsWaitingForPathEstimate = false;
            agent.BB.ClearMoveTarget();
            if (agent.ActionMemory is Memory mem) {
                mem.Cleanup(agent);
            }
            agent.BB.RemoveActiveReservation(goal);
            if (AIGoal.IsCompleted(goal)) {
                TryWalkAway(agent);
            }
        }
    }
}
