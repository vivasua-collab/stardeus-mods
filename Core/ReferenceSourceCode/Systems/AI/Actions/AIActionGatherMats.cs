#if !PRODUCTION_BUILD
// #define DEBUG_AI_DELIVERY
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Inventory;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionGatherMats : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionGatherMats());
        }
        public const string ActId = "GatherMats";
        private AIActionGatherMats() : base(ActId, () => T.ActGatherrawmaterials) {
            Preconditions = AIState.With(AIVarsH.IsResourcesDelivered, false);
            Outcomes = AIState.With(AIVarsH.IsResourcesDelivered, true);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }
        public enum Phase {
            CheckReachability,
            EvaluateRequest,
            SearchForMat,
            CollectMatFromStorage,
            BringCollectedMat,
            LoadCollectedMat,
            DeliverToStops,
        }
        public sealed class Memory : ISaveable {
            // Saveable
            public Phase Phase;
            public int ProvideMatAttempts;
            // Temporal
            public int Estimations;
            public HashSet<IMatStorage> FailedStorages;
            public UnstoredMatComp CarriedMat;
            public IMatRequester MatRequester;
            public MatRequest SeekedMatReq;
            public IMatStorage SeekedStorage;
            public int SeekedStoragePos = -1;
            // Walked up to SeekedStorage a second time because the first walk ended out of reach
            public bool RetriedApproach;
            public int ReservedAmt;
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
            public readonly List<UnstoredMatComp> CarriedForStops = new(1);
            public int CurrentMatReqMissingAmt {
                get {
                    var collectedAmt = (CarriedMat?.StackSize) ?? 0;
                    var wantedAmt = SeekedMatReq.Amount;
                    if (Batch != null) {
                        wantedAmt += Batch.ExtraFor(SeekedMatReq.Type);
                    }
                    var missingAmt = Maths.Max(0, wantedAmt - collectedAmt);
                    return missingAmt;
                }
            }
            public bool CheckRequest(AIGoal goal) {
                if (SeekedMatReq == null) {
                    // This happens after save / load, but there is no great
                    // solution for it, so we will just succeed the task for now
                    // D.Warn("Seeked Mat Req is null: {0}", goal);
                    return false;
                }
                if (SeekedMatReq.IsCancelled || SeekedMatReq.Amount < 1) {
                    // #if !PRODUCTION_BUILD
                    // D.Warn("Seeked Mat Req cancelled? {0} | {1}", goal,
                    //     D.DumpInstance(SeekedMatReq));
                    // #endif
                    return false;
                }
                return true;
            }

            public void Load(ComponentData data) {
                ProvideMatAttempts = data.GetInt("Attempts", 0);
            }

            public ComponentData Save() {
                var data = new ComponentData(0, null);
                data.SetInt("Attempts", ProvideMatAttempts);
                return data;
            }

            public static bool For(AIAgentComp agent, AIGoal goal) {
                var mem = new Memory();
                #if DEBUG_AI_DELIVERY
                D.Warn("Creating memory for delivery action for: {0} => {1}",
                    goal, agent);
                #endif
                if (agent.ActionMemoryData != null) {
                    // we have loaded a save and we will restore the ActionMemory
                    mem.Load(agent.ActionMemoryData);
                    agent.ActionMemoryData = null;
                }
                if (goal.Target is IMatRequester mreq) {
                    mem.MatRequester = mreq;
                    mem.SeekedMatReq = mreq.NextMissingMat;
                } else {
                    D.Err("Goal target is not a IMatRequester: {0} -> {1}",
                        goal.Target, goal);
                    return false;
                }
                agent.ActionMemory = mem;
                return true;
            }

            public void Cleanup(AIAgentComp agent) {
                agent.Motion.OnPathEstimate = null;
                Batch?.ReleaseAll();
                Batch = null;
                if (CarriedMat != null) {
                    CarriedMat.ReaddToInventoryIfNeeded();
                    agent.Being.Graphics.DropIfCarrying();
                    CarriedMat = null;
                }
                if (ReservedAmt > 0 && SeekedStorage != null) {
                    SeekedStorage.CancelReservationFor(agent.Being, null);
                }
            }
        }

        private AIActionResult SuccessFor(AIGoal goal) {
            // D.Warn("Success for {0}", goal);
            goal.WithWorld(AIVarsH.IsResourcesDelivered, true);
            return Success;
        }
        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (!TryGetMemory(agent, goal, out var mem)) {
                return Error("Failed getting AI task memory for ActionDeliverResources");
            }

            if (!mem.CheckRequest(goal)) {
                return SuccessFor(goal);
            }

            var phase = mem.Phase;

            if (agent.BB.MoveProgress != null) {
                if (agent.BB.MoveProgress.IsMoving) {
                    if (phase != Phase.CollectMatFromStorage
                            || !InventorySys.IsForbiddenFor(mem.SeekedStorage, agent.Being)) {
                        return InProgress;
                    }
                    // Forbidden while we were on the way: turn back and look for another
                    // source. It is not out of reach, so no nav failure or flyer mark
                    agent.Motion.Abort(T.AdjForbidden);
                    if (mem.ReservedAmt > 0) {
                        mem.SeekedStorage.CancelReservationFor(agent.Being, null);
                        mem.ReservedAmt = 0;
                    }
                    mem.SeekedStorage = null;
                    mem.Phase = phase = Phase.SearchForMat;
                } else if (agent.BB.MoveProgress.HasFailed) {
                    if (phase == Phase.CollectMatFromStorage) {
                        agent.BB.MoveProgress = null;
                        // failed to reach the storage
                        var lastStoragePos = mem.SeekedStoragePos;
                        if (!IgnoreAndKeepSearching(agent, mem)) {
                            // we ignored too many storages, let's fail the whole task
                            if (agent.Motion.IsGrounded) {
                                return FailedSoft(T.AdRejUnreachableMatGrounded);
                            }
                            return FailedNavToMat(agent, lastStoragePos);
                        }
                        mem.Phase = phase = Phase.SearchForMat;
                    } else if (phase == Phase.DeliverToStops) {
                        // only that stop is given up, the trip goes on
                        mem.Batch?.OnWalkFailed();
                    } else {
                        if (agent.BB.MoveTargetPos > 0) {
                            // failed to walk to the requester?
                            return FailedNavToTargetWithoutStore(agent);
                        } else {
                            return Failed(T.AdRejTargetUnavailable);
                        }
                    }
                }
                // we walked!
                agent.BB.MoveProgress = null;
                agent.BB.MoveTargetPos = Pos.Invalid;
            }

            switch (phase) {
                case Phase.CheckReachability: return CheckReachability(goal, agent, mem);
                case Phase.EvaluateRequest: return EvaluateRequest(goal, agent, mem);
                case Phase.SearchForMat: return SearchForMat(goal, agent, mem);
                case Phase.CollectMatFromStorage: return CollectMatFromStorage(goal, agent, mem);
                case Phase.BringCollectedMat: return BringCollectedMat(goal, agent, mem);
                case Phase.LoadCollectedMat: return LoadCollectedMat(goal, agent, mem);
                case Phase.DeliverToStops: return DeliverToStops(agent, mem);
            }

            D.Err("Unhandled phase: {0}", phase);
            return Error("Unhandled deliver resources phase: {0}", phase);
        }
        private AIActionResult LoadCollectedMat(AIGoal goal, AIAgentComp agent, Memory mem) {
            int step = 0;
            try {
                if (!CheckMatRequesterReachable(goal, agent, mem, out
                        bool targetUnavailable, out int targetPosIdx)) {
                    if (targetUnavailable) {
                        return Failed(T.AdRejTargetUnavailable);
                    }
                    //return FailReachMatRequester();
                    return FailedNavToTarget(agent, targetPosIdx);
                }
                step = 1;

                if (mem.CarriedMat == null) {
                    D.Err("Trying to load collected mat while not carrying anything!");
                    mem.Phase = Phase.EvaluateRequest;
                    return InProgress;
                }
                step = 2;

                var matRequester = mem.MatRequester;

                step = 3;

                if (matRequester == null || matRequester.Entity == null
                        || !matRequester.Entity.IsActive) {
                    D.ErrSoft("Lost MatRequester, failing ActGatherMat: {0}-> {1}",
                        matRequester, goal);
                    return Failed(T.AdRejTargetUnavailable);
                }

                step = 4;

                // We have actually reached the mat requester
                if (mem.CarriedMat != null) {
                    agent.Being.Graphics?.Drop(mem.CarriedMat.Obj, true);
                }
                step = 5;
                var amt = mem.CarriedMat?.StackSize ?? 0;
                step = 6;
                if (amt == 0) {
                    D.Err("Carried mat is now 0 stack size after carry! {0}", mem.CarriedMat);
                    mem.Phase = Phase.EvaluateRequest;
                    //mem.CarriedMat.ReaddToInventoryIfNeeded();
                    mem.CarriedMat = null;
                    return InProgress;
                }
                step = 7;
                if (matRequester.Entity != null) {
                    step = 8;
                    Mat.PlayProvideVFX(agent.S, agent.Being.Position,
                        matRequester.Entity.Position, amt);
                }
                step = 9;
                var isFullfilled = matRequester.ProvideRequestedMat(mem.CarriedMat, agent.Being);
                // What the requester actually accepted: its Take shrinks the carried
                // pile by exactly that much
                var took = amt - (mem.CarriedMat?.StackSize ?? 0);
                if (took > 0) {
                    mem.HasDelivered = true;
                }
                step = 10;
                mem.CarriedMat?.ReaddToInventoryIfNeeded();
                step = 11;
                if (isFullfilled) {
                    step = 12;
                    mem.CarriedMat = null;
                    // streamline only carriers - anyone who can build the tile gets the
                    // construct task pre-assigned instead (a Build tool on a Dumb Core is no builder)
                    step = 13;
                    if (agent.Motion.IsFlying && !agent.Jobs.CanDo(JobTypeIdH.Construction)) {
                        step = 14;
                        TryStreamline(agent, goal);
                    }
                    step = 15;
                    return SuccessFor(goal);
                } else if (took < 1) {
                    // The requester refused what we brought - that is the loop this
                    // cap exists for
                    if (mem.ProvideMatAttempts++ > 20) {
                        step = 16;
                        D.Err("Tried providing material 20x and could not satisfy requester: {0} -> {1}. {2}",
                            mem.CarriedMat.Type, matRequester.Entity, this);
                        step = 17;
                        mem.CarriedMat = null;
                        return Failed(T.OperationFailed);
                    }
                } else {
                    // Delivered, and the requester still wants more (a refillable under
                    // continuous burn re-grows its request every tick). A productive run
                    // is not a stuck one, so it must not count towards the error above -
                    // but one worker should not ferry forever either. Hand the task back
                    // to the auction after a while; the requester re-creates it if it is
                    // still short.
                    mem.ProvideMatAttempts = 0;
                    if (++mem.TripsDelivered >= 20) {
                        step = 16;
                        mem.CarriedMat = null;
                        return SuccessFor(goal);
                    }
                }
                step = 18;
                // we haven't fullfilled the request, but the old one will be cancelled
                // so let reload it here
                mem.CarriedMat = null;
                RefreshMatReq(mem);
                step = 19;
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading gathered materials (handled). STEP: {0} | {1} | Cur act: {2}",
                    step, agent, agent.CurrentAction);
                return Failed(T.AdRejInterrupted);
            }
        }

        private AIActionResult BringCollectedMat(AIGoal goal, AIAgentComp agent, Memory mem) {
            if (mem.CarriedMat == null) {
                D.Err("Trying to bring collected mat while not carrying anything!");
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            }
            // The other blueprints of the trip come first, the task's own one last
            if (mem.Batch != null && mem.Batch.HasStops) {
                mem.Phase = Phase.DeliverToStops;
                return InProgress;
            }

            var matRequester = mem.MatRequester;
            var worker = agent.Being;
            // Navigate to target
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
                        // during construction material delivery can happen without in port
                        // D.Err("ActGatherMat for tile that doesn't have an InPort: {0}",
                        //     t.Definition.Id);
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
                            PathReqFlags.AdjOnTop | PathReqFlags.TempDestination, out agent.BB.MoveProgress)) {
                        agent.BB.MoveTargetPos = matRequester.InPort;
                    } else {
                        FailedNavToTarget(agent, matRequester.InPort);
                    }
                }
            }

            // and then load
            mem.Phase = Phase.LoadCollectedMat;
            return InProgress;
        }
        private AIActionResult DeliverToStops(AIAgentComp agent, Memory mem) {
            if (mem.Batch == null) {
                mem.Phase = Phase.BringCollectedMat;
                return InProgress;
            }
            var carried = mem.CarriedForStops;
            carried.Clear();
            if (mem.CarriedMat != null) {
                carried.Add(mem.CarriedMat);
            }
            var result = mem.Batch.Step(agent, carried);
            mem.CarriedMat = carried.Count > 0 ? carried[0] : null;
            carried.Clear();
            if (result == ConstructionDeliveryBatch.StepResult.Done) {
                mem.Batch.ReleaseAll();
                mem.Phase = mem.CarriedMat != null
                    ? Phase.BringCollectedMat : Phase.EvaluateRequest;
            }
            return InProgress;
        }

        private bool CheckMatRequesterReachable(AIGoal goal, AIAgentComp agent, Memory mem,
                out bool targetUnavailable, out int targetPosIdx) {
            var matRequester = mem.MatRequester;
            targetUnavailable = false;
            if (matRequester == null) {
                D.Err("Mat requester null! {1} -> {0}", goal, goal.Target);
                targetUnavailable = true;
                targetPosIdx = Pos.Invalid;
                return false;
            }
            if (!matRequester.Entity.IsActive) {
                D.Warn("Mat Requester no longer active");
                targetUnavailable = true;
                targetPosIdx = Pos.Invalid;
                return false;
            }
            // sanity check
            if (matRequester.InPort != 0) {
                targetPosIdx = matRequester.InPort;
                if (Pos.SqrDistance(matRequester.InPort, agent.PosIdx) > 2) {
                    D.ErrSoft("Trying to load collected mat while being too far from the requester! r: {0}, w: {1}. {2}",
                        Pos.String(matRequester.InPort), Pos.String(agent.PosIdx), matRequester.Entity, goal);
                    return false;
                }
            } else {
                // Maybe not?
                if (matRequester.Entity is Tile t) {
                    t = EntityUtils.SelfOrBlockingObjAbove(t);
                    targetPosIdx = t.PosIdx;
                    if (!EntityUtils.IsNearObj(agent.PosIdx, t)) {
                        D.ErrSoft("Trying to load collected mat while being too far from the requester tile! r: {0}, w: {1} | {2} | {3}",
                            Pos.String(t.CenterIdx), Pos.String(agent.PosIdx), t, goal);
                        return false;
                    }
                } else if (matRequester.Entity is Being b) {
                    targetPosIdx = b.PosIdx;
                    if (Vectors.SqrDistance(b.Position, agent.Motion.Position.Exact) > 2) {
                        return false;
                    }
                } else {
                    D.Err("matRequester is not a tile or being, and MatReqPos is 0!");
                    targetPosIdx = Pos.Invalid;
                    targetUnavailable = true;
                    return false;
                }
            }
            return true;
        }

        private AIActionResult CollectMatFromStorage(AIGoal goal, AIAgentComp agent, Memory mem) {
            if (mem.SeekedStorage == null || !mem.SeekedStorage.Entity.IsActive) {
                mem.SeekedStorage = null;
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            }
            if (mem.SeekedStoragePos != mem.SeekedStorage.OutPort) {
                // object could be floating in space
                if (agent.S.Sys.Ship.IsMoving && EntityUtils.IsEmptySpaceAt(mem.SeekedStorage.OutPort)) {
                    if (TryGoToStorage(agent, mem)) {
                        // remain in same phase
                        return InProgress;
                    }
                } else {
                    D.ErrSoft("Seeked storage and seeked storage pos idx do not match! {0} / {1} / {2} / {3}",
                        this, agent, D.DumpInstance(mem), mem.SeekedStorage);
                }
                IgnoreAndKeepSearching(agent, mem);
                mem.SeekedStorage = null;
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            }
            var seekedStorage = mem.SeekedStorage;
            // At this point we have (hopefully) navigated to the storage where
            // our sought material is at. Let's try to pick it up and move
            // towards where we have to go

            var worker = agent.Being;
            var matReq = mem.SeekedMatReq;

            if (matReq == null || matReq.IsCancelled) {
                // why was this a failure?
                // return Failure(T.AdRejCancelled);
                return SuccessFor(goal);
            }

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
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            }

            // We have reached successfully, let's grab the good stuff
            var takenAmt = seekedStorage.Take(matReq.Type, worker, mem.ReservedAmt, null);
            if (takenAmt != mem.ReservedAmt) {
                // This happens at fast speed when someone else takes the pile first
                agent.BB.RemoveActiveReservation(this);
                /*
                D.Err("ActGatherMat: Tried to take {0} {1} but took {2} from {3}",
                    reservedAmt, seekedMatReq.Type, takenAmt, seekedStorage);
                */
                if (takenAmt == 0) {
                    mem.Phase = Phase.EvaluateRequest;
                    return InProgress;
                }
            }
            mem.ReservedAmt = 0;

            // We have taken some stuff out from storage, now we have to turn that
            // into a physical obj for the duration of the haul
            if (mem.CarriedMat != null) {
                // grow the snowball
                mem.CarriedMat.StackSize += takenAmt;
                mem.CarriedMat.MaxStackSize = mem.CarriedMat.StackSize;
            } else {
                // start carrying a newly spawned mat
                mem.CarriedMat = EntityUtils.SpawnRawMaterial(new Mat() {
                    StackSize = takenAmt,
                    MaxStackSize = takenAmt,
                    Type = matReq.Type
                }, worker.PosIdx, 0, true, false);
                if (mem.CarriedMat == null || mem.CarriedMat.StackSize < 1) {
                    D.Err("Trying to carry material with stack size 0: {0}", mem.CarriedMat);
                    return Failed(T.AdRejTargetUnavailable);
                }
                mem.CarriedMat.RemoveFromInventory();
                if (!worker.Graphics.Carry(mem.CarriedMat.Obj, false, false)) {
                    D.Err("Failed carrying gathered mat {0} {1}", mem.CarriedMat, this);
                    return Failed(T.AdRejTargetUnavailable);
                }
            }

            if (mem.CurrentMatReqMissingAmt > 0
                    && agent.SpareCarryAmount > 0) {
                mem.Phase = Phase.SearchForMat;
            } else {
                // we have it all
                mem.Phase = Phase.BringCollectedMat;
            }
            return InProgress;
        }

        private AIActionResult SearchForMat(AIGoal goal, AIAgentComp agent, Memory mem) {
            var maxCarry = agent.SpareCarryAmount;
            if (maxCarry < 1) {
                if (mem.CarriedMat != null) {
                    mem.Phase = Phase.BringCollectedMat;
                    return InProgress;
                }
                return Failed(T.AdRejInterrupted);
            }
            var toReserve = mem.CurrentMatReqMissingAmt;
            if (toReserve < 1) {
                if (maxCarry < 1) {
                    mem.Phase = Phase.BringCollectedMat;
                    return InProgress;
                }

                // nothing we can do now
                return SuccessFor(goal);
            }

            var worker = agent.Being;
            var matReq = mem.SeekedMatReq;
            if (matReq?.Type == null) {
                D.Err("MatReq null or with mat type null! {0} | {1} | {2}",
                    matReq, goal, goal.Target);
                return Failed(T.AdRejTargetUnavailable);
            }

            // Only keep fetching what nobody stronger is waiting on. The doability gate
            // ran once when the goal started; a long trip loop would otherwise keep
            // taking every unit produced from under a higher priority claim
            // (see MaterialClaims.md).
            var inventory = worker.S.Sys.Inventory;
            if (inventory.AvailableFor(matReq.Type, mem.MatRequester,
                    goal.GoalPriority) < 1) {
                if (mem.CarriedMat != null) {
                    mem.Phase = Phase.BringCollectedMat;
                    return InProgress;
                }
                if (mem.HasDelivered) {
                    return SuccessFor(goal);
                }
                if (inventory.AllowedCount(matReq.Type) > 0) {
                    return FailedMatReserved();
                }
                // Nothing in stock at all - reported as lacking below
            }

            mem.SeekedStorage = inventory.FindClosestReachableStorage(
                    matReq.Type, worker, mem.FailedStorages, false);

            if (mem.SeekedStorage == null) {
                if (matReq.IsAmountOptional && mem.CarriedMat != null) {
                    // OK, let's bring what we have
                    mem.Phase = Phase.BringCollectedMat;
                    return InProgress;
                }
                if (mem.HasDelivered) {
                    // Everything the colony had is already in the requester. Failing
                    // here would pile a growing retry backoff onto a task that did make
                    // progress, and the next partial batch has to be picked up promptly
                    // (see MaterialClaims.md) - so end the trip as done instead.
                    return SuccessFor(goal);
                }
                worker.Motion.StoreNavFailureAt(goal.Target.Entity.PosIdx);
                if (inventory.AllowedCount(matReq.Type) > 0) {
                    // The stock exists, this worker just cannot get to it - a
                    // shortage report here would send the player hunting for a
                    // production problem that does not exist
                    inventory.MarkUnreachableByFlying(matReq.Type, worker);
                    return FailedUnreachableMat(agent, matReq.Type);
                }
                return FailWithNotifyLackOf(worker.S, matReq.Type,
                    matReq.Amount, mem.MatRequester);
            }

            if (toReserve > maxCarry) {
                toReserve = maxCarry;
            }

            mem.ReservedAmt = mem.SeekedStorage.Reserve(
                    matReq.Type, worker, toReserve, null);

            if (mem.ReservedAmt < 1) {
                mem.ReservedAmt = 0;
                // We could not reserve anything in given storage
                if (!IgnoreAndKeepSearching(agent, mem)) {
                    return FailedUnreachableStorage(agent,
                        goal.Target.Entity.PosIdx);
                }
                mem.Phase = Phase.SearchForMat;
                return InProgress;
            }

            mem.SeekedStoragePos = mem.SeekedStorage.OutPort;
            mem.RetriedApproach = false;

            // All good, let's go to next phase
            // Note, hard fail for sub action is still handled as soft failure
            // within scope of this task
            if (mem.SeekedStorage.OutPort != worker.PosIdx) {
                if (!TryGoToStorage(agent, mem)) {
                    if (!IgnoreAndKeepSearching(agent, mem)) {
                        return FailedUnreachableStorage(agent, agent.BB.MoveTargetPos);
                    }
                    mem.Phase = Phase.SearchForMat;
                    return InProgress;
                }
            }

            // After we are doing navigating to the storage, let's carry the
            // shit where it has to go
            mem.Phase = Phase.CollectMatFromStorage;
            return InProgress;
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

        private AIActionResult EvaluateRequest(AIGoal goal, AIAgentComp agent, Memory mem) {
            RefreshMatReq(mem);
            // Everything is fulfilled
            if (mem.SeekedMatReq == null || mem.SeekedMatReq.Amount < 1) {
                return SuccessFor(goal);
            }
            if (!mem.IsBatchFormed) {
                mem.IsBatchFormed = true;
                mem.Batch = ConstructionDeliveryBatch.TryForm(agent, goal,
                    mem.MatRequester, mem.CarriedMat?.StackSize ?? 0);
            }

            // Let's go find the material
            mem.Phase = Phase.SearchForMat;
            return InProgress;
        }

        private static void RefreshMatReq(Memory mem) {
            mem.SeekedStorage = null;
            mem.SeekedMatReq = mem.MatRequester.NextMissingMat;
        }

        private AIActionResult CheckReachability(AIGoal goal, AIAgentComp agent, Memory mem) {
            if (!Ready.Pathfinding) { return InProgress; }
            var worker = agent.Being;
            if (worker.Motion.IsBusy) {
                return InProgress;
            }
            if (!agent.BB.HasRequestedReachability) {
                agent.BB.IsTargetReachable = true;
                var matReq = mem.MatRequester;
                if (!matReq.Entity.IsActive) {
                    D.ErrSoft("mat requested inactive: {0}", matReq.Entity);
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (matReq.InPort == 0) {
                    if (matReq.Entity is Tile t) {
                        if (t.PosIdx == 0) {
                            D.Err("MatRequester Tile has pos 0! {0}", t);
                            return Failed(T.AdRejTargetUnavailable);
                        }
                        EstimatePathTo(agent, mem, t);
                    } else {
                        if (matReq.Entity == worker) {
                            // clothing task?
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
                    D.Err("AI Act collect mat id {0} estimations in 2 hours with no result. Giving up! {0} {1}",
                        mem.Estimations, worker, this);
                    return FailedNavToTarget(agent);
                }
                return InProgress;
            }
            if (agent.BB.IsTargetReachable) {
                mem.Phase = Phase.EvaluateRequest;
                return InProgress;
            }
            // FIXME figure out why move target pos can be 0 here. it surely can be.
            return FailedNavToTarget(agent, false);
        }

        private void EstimatePathTo(AIAgentComp agent, Memory mem, Tile t) {
            mem.Estimations++;
            agent.BB.HasRequestedReachability = true;
            agent.BB.MoveTargetPos = t.PosIdx;
            if (agent.BB.IsWaitingForPathEstimate) {
                D.Err("Trying to run new path estimate to tile while waiting for one! {0} {1}",
                    agent, this);
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
            #if DEBUG_AI_DELIVERY
            D.Log("Asking for path estimate to tile: {0} -> {1}", agent, this);
            #endif
            agent.BB.IsWaitingForPathEstimate = true;
            if (!agent.Motion.EstimatePathTo(t,
                    (res, _) => OnPathEstimated(res, agent, mem))) {
                agent.BB.IsTargetReachable = false;
                agent.BB.IsWaitingForPathEstimate = false;
            }
        }

        private void EstimatePathTo(AIAgentComp agent, Memory mem, int pos) {
            if (pos == 0) {
                D.Err("Trying to estimate path pos to 0 in GatherMat: {0}!", agent);
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
                D.Err("Trying to run new path estimate while waiting for one! {0} {1}",
                    agent, this);
                return;
            }
            if (agent.Motion.HadNavFailureAt(pos)) {
                agent.BB.IsTargetReachable = false;
                agent.BB.IsWaitingForPathEstimate = false;
                return;
            }
            //D.Log("Asking for path estimate: {0} -> {1}", worker, ad);
            agent.BB.MoveTargetPos = pos;
            agent.BB.IsWaitingForPathEstimate = true;
            if (!agent.Motion.EstimatePathTo(pos,
                    (res, _) => OnPathEstimated(res, agent, mem))) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = false;
            }
        }
        private void OnPathEstimated(PathRes res, AIAgentComp agent, Memory mem) {
            //waitingForEstimateTicks = 0;
            if (agent.BB.IsWaitingForPathEstimate) {
                agent.BB.IsWaitingForPathEstimate = false;
                agent.BB.IsTargetReachable = res.IsValid && res.IsSafe;
                //D.Log("Estimated path: {0}", res);
                if (!agent.BB.IsTargetReachable) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                }
            } else {
                // it's ok if the agent was interrupted
                if (agent.CurrentAction != null) {
                    D.Err("Received path estimate while wasn't waiting for one! {0} | {1} | CurAct: {2}",
                        res, agent, agent.CurrentAction);
                }
            }
        }
        private bool IgnoreAndKeepSearching(AIAgentComp agent, Memory mem) {
            var seekedStorage = mem.SeekedStorage;
            D.Ass(seekedStorage != null,
                "ActGatherMat: Seeked storage is null while trying to ignore it!");
            mem.FailedStorages ??= new();
            if (!mem.FailedStorages.Add(seekedStorage)) {
                D.Err("AI collect mats: Seeked storage already in ignore list: {0}, {1}",
                    seekedStorage, D.Dump(mem.FailedStorages));
            }
            var worker = agent.Being;
            worker.Motion.StoreNavFailureAt(seekedStorage.OutPort);
            if (mem.ReservedAmt > 0) {
                seekedStorage.CancelReservationFor(agent.Being, null);
                if (seekedStorage is UnstoredMatComp pile) {
                    pile.Obj.Transform.UnstuckIfNeeded();
                }
                if (!worker.Motion.IsGrounded) {
                    seekedStorage.MarkAsUnreachableByFlying(worker);
                }

                mem.ReservedAmt = 0;
            }
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
                D.Err("Wrong or missing memory on the AIAgentComp: {0} -> {1}",
                    agent, agent.ActionMemory);
                return false;
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Yield to AIActionGatherMatsMulti for multi-material requests
            if (goal.Target is IMatRequester req && req.IsMulti) {
                return 0f;
            }
            return 2f * agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
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
