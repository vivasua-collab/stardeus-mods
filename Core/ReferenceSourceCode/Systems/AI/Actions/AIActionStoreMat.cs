using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Input;

namespace Game.Systems.AI {
    public sealed class AIActionStoreMat : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStoreMat());
        }
        public const string ActId = "StoreMat";
        // Deconstruction keeps dropping piles within reach of the first one, and a hauler
        // with room to spare kept collecting them for hours without storing anything
        // (STA-1631). See Docs/Architecture/Systems/MixedTypeHauling.md
        private const int MaxCollectTicks = Consts.TicksPer6Hours;
        // Extras are only collected this many tiles from the first pile
        private const int MaxExtraDist = 25;
        private AIActionStoreMat() : base(ActId, () => T.ActionStore) {
            Preconditions = AIState.With(AIVarsH.IsMatStored, false);
            Outcomes = AIState.With(AIVarsH.IsMatStored, true);
            WithRequiredJobType(JobTypeIdH.Hauling);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            MarkAsMobile();
        }
        public enum Phase {
            SeekMat,
            SeekMatExtra,
            MoveToStorage,
            MoveAside,
            PickUp,
            LoadCollectedMat,
            Done,
        }
        public sealed class Memory : ISaveable {
            private const string ExtraIdsKey = "ExtraIds";
            // Only the carried extra piles are saved, see Save
            public Phase Phase;
            public bool IsPickingUpExtra;
            public HashSet<IMatStorage> IgnoredPiles;
            public UnstoredMatComp CarriedMat;
            // Carried piles of other types than CarriedMat, stacked with CarryController
            public List<UnstoredMatComp> ExtraMats;
            public UnstoredMatComp TargetMat;
            // Type of the pile that is being picked up now, the first one is CarriedMat.Type
            public MatType TargetMatType;
            public List<IMatStorage> CompatibleStorages;
            public IMatStorage SeekedStorage;
            // Where the first pile goes. Extras of other types are only picked up if this
            // storage accepts them, so the trip ends with a single stop
            public IMatStorage PlannedStorage;
            // Extras are searched around the first pile rather than around the worker,
            // otherwise a chain of pickups can drift away from the ship
            public int ChainOrigin = Pos.Invalid;
            // When the first pile was picked up; collecting extras stops MaxCollectTicks later
            public long FirstPickupTick;
            // Whether the goal of the extra pile being picked up was cancelled by us
            public bool CancelledTargetGoal;
            public int ReservedAmt;
            private List<int> loadedExtraIds;

            public void Load(ComponentData data) {
                var idStr = data.GetString(ExtraIdsKey, null);
                if (idStr == null) { return; }
                var parts = idStr.Split(',');
                loadedExtraIds = new List<int>(parts.Length);
                for (int i = 0; i < parts.Length; i++) {
                    if (int.TryParse(parts[i], out var id)) {
                        loadedExtraIds.Add(id);
                    }
                }
            }

            // Carrying is not saved, so carried extras load as loose piles without a
            // goal, as does the extra pile being walked to. Their ids are kept so they
            // can be ordered stored after loading.
            public ComponentData Save() {
                var hasPending = HasPendingTarget;
                if (Arrays.IsEmpty(ExtraMats) && !hasPending) {
                    return null;
                }
                var sb = new System.Text.StringBuilder();
                if (ExtraMats != null) {
                    for (int i = 0; i < ExtraMats.Count; i++) {
                        if (sb.Length > 0) { sb.Append(','); }
                        sb.Append(ExtraMats[i].EntityId);
                    }
                }
                if (hasPending) {
                    if (sb.Length > 0) { sb.Append(','); }
                    sb.Append(TargetMat.EntityId);
                }
                var data = new ComponentData(0, null);
                data.SetString(ExtraIdsKey, sb.ToString());
                return data;
            }

            private void OrderStoreLoadedExtras(GameState s) {
                if (loadedExtraIds == null) { return; }
                for (int i = 0; i < loadedExtraIds.Count; i++) {
                    // Missing if it was merged or taken since the save
                    s.Components.Find<UnstoredMatComp>(loadedExtraIds[i])?.OrderStoreIfLoose();
                }
                loadedExtraIds = null;
            }

            // The extra pile being walked to lost its goal to SeekMatExtra
            private bool HasPendingTarget => CancelledTargetGoal && TargetMat != null
                && TargetMat.Entity.IsActive && !TargetMat.Entity.IsCarriedByAny;

            public void ReorderCancelledTarget() {
                if (HasPendingTarget) {
                    TargetMat.OrderStoreIfLoose();
                }
                CancelledTargetGoal = false;
            }

            public UnstoredMatComp FindExtra(MatType type) {
                if (ExtraMats == null) { return null; }
                for (int i = 0; i < ExtraMats.Count; i++) {
                    // A carried pile can be destroyed on the way (fire, explosion)
                    if (ExtraMats[i].Type == type && ExtraMats[i].Entity.IsActive) {
                        return ExtraMats[i];
                    }
                }
                return null;
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
                    mem.OrderStoreLoadedExtras(agent.S);
                }
                if (goal.Target is UnstoredMatComp pile) {
                    mem.TargetMat = pile;
                    mem.TargetMatType = pile.Type;
                } else {
                    // ok if pile was consumed
                    if (goal.Target != null) {
                        D.Err("Goal target is not a UnstoredMatComp: {0} -> {1}",
                            goal.Target, goal);
                    }
                    return false;
                }
                agent.ActionMemory = mem;
                return true;
            }

            public void Cleanup(AIAgentComp agent) {
                var hasExtras = !Arrays.IsEmpty(ExtraMats);
                if (hasExtras) {
                    // Detach without merging, so every extra keeps its identity and can
                    // be ordered stored below
                    agent.Being.Graphics.CarryCtrl?.DropAllAdditional(skipInventoryCheck: true);
                }
                if (CarriedMat != null) {
                    CarriedMat.ReaddToInventoryIfNeeded();
                    agent.Being.Graphics.DropIfCarrying();
                    CarriedMat = null;
                } else {
                    if (agent.Being.Graphics.IsCarrying()) {
                        D.Err("Supposed to drop carrying but carriedMat is null! Will force drop this: {0}. {1} {2}",
                            agent.Being.Graphics.Carrying, agent.Goal, agent.Being);
                        agent.Being.Graphics.DropIfCarrying();
                    }
                }
                if (hasExtras) {
                    // An extra pile must not end up worse off than before it was picked
                    // up, so each one gets its own storing order back
                    for (int i = 0; i < ExtraMats.Count; i++) {
                        var extra = ExtraMats[i];
                        if (!extra.Entity.IsActive) { continue; }
                        extra.ReaddToInventoryIfNeeded();
                        extra.UpdateForbiddenIcon(true);
                        extra.OrderStoreLaterIfLoose();
                    }
                    ExtraMats.Clear();
                }
                if (HasPendingTarget) {
                    TargetMat?.OrderStoreLaterIfLoose();
                }
                CancelledTargetGoal = false;
            }
        }

        private AIActionResult SuccessFor(AIGoal goal, AIAgentComp agent) {
            TryWalkAway(agent);
            goal.WithWorld(AIVarsH.IsMatStored, true);
            return Success;
        }
        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (!TryGetMemory(agent, goal, out var mem)) {
                return Error("Failed getting AI task memory for AIActionStoreMat");
            }

            var phase = mem.Phase;
            var worker = agent.Being;

            if (agent.BB.MoveProgress != null) {
                if (agent.BB.MoveProgress.IsMoving) {
                    return InProgress;
                }
                if (agent.BB.MoveProgress.HasFailed) {
                    if (phase == Phase.PickUp) {
                        agent.BB.MoveProgress = null;
                        if (mem.TargetMat != null) {
                            worker.Motion.StoreNavFailureAt(mem.TargetMat.OutPort);
                            if (!agent.Being.Graphics.IsCarrying()) {
                                return FailedNavToMat(agent, mem.TargetMat.OutPort);
                            }
                            mem.Phase = Phase.SeekMatExtra;
                            (mem.IgnoredPiles ??= new()).Add(mem.TargetMat);
                            GiveUpExtra(agent, mem, "Could not reach extra mat");
                            if (worker.Motion.IsFlying) {
                                mem.TargetMat.MarkAsUnreachableByFlying(worker);
                            }
                        }
                    } else if (phase == Phase.MoveToStorage) {
                        // nothing to do, we will retry moving to a different storage
                    } else {
                        return FailedNavToTarget(agent);
                    }
                }
                // we walked!
                agent.BB.MoveProgress = null;
                agent.BB.MoveTargetPos = Pos.Invalid;
            }

            // Not the local: a failed walk to an extra pile moves on to SeekMatExtra above
            // and drops the reservation, and PickUp would then take 0 from that pile
            switch (mem.Phase) {
                case Phase.SeekMat: return SeekMat(goal, agent, mem);
                case Phase.PickUp: return PickUp(goal, agent, mem);
                case Phase.SeekMatExtra: return SeekMatExtra(goal, agent, mem);
                case Phase.MoveToStorage: return MoveToStorage(goal, agent, mem);
                case Phase.MoveAside: return MoveAside(goal, agent, mem);
                case Phase.LoadCollectedMat: return LoadCollectedMat(goal, agent, mem);
                case Phase.Done: return SuccessFor(goal, agent);
            }

            D.Err("Unhandled phase: {0}", phase);
            return Error("Unhandled deliver resources phase: {0}", phase);
        }

        private AIActionResult SeekMat(AIGoal goal, AIAgentComp agent, Memory mem) {
            var worker = agent.Being;
            if (!mem.TargetMat.Entity.IsActive) {
                mem.Phase = Phase.Done;
                return InProgress;
            }
            if (mem.TargetMat.IsFullyReserved) {
                if (mem.TargetMat.HasReservation(worker)) {
                    D.Err("Unexpected reservation on a mat {0}", goal);
                    return Failed(T.AdRejUndefined);
                }
                mem.Phase = Phase.Done;
                return InProgress;
            }

            var maxCarry = agent.SpareCarryAmount;
            if (maxCarry < 1) {
                D.Err("Being has zero carry capacity: Carrying: {0} | Agent: {1}!",
                    agent.Being.Graphics.Carrying, agent);
                // can't carry any more
                return Failed(T.AdRejCouldNotReserve);
            }

            var toReserve = Maths.Min(maxCarry, mem.TargetMat.StackSize);
            LoadCompatibleStorages(mem, agent.Being, mem.TargetMat.Type, toReserve);

            if (Arrays.IsEmpty(mem.CompatibleStorages)) {
                var isGrounded = agent.Motion.IsGrounded;
                // check if we need to move aside
                var mustMoveAside = EntityUtils.ObjAt(mem.TargetMat.OutPort) != null
                    || EntityUtils.WallAt(mem.TargetMat.OutPort) != null;
                // perhaps we just can't go to the storage?
                // A storage exists, but this worker can't reach it, enter it safely or
                // path to it right now. That is not "no storage": another worker may
                // store the pile, so hand it back without failing it for everyone, and
                // keep this worker from winning it again every tick. A flyer still moves
                // a pile off an object first.
                if ((isGrounded || !mustMoveAside)
                        && agent.S.Sys.Inventory.HasCompatibleStorages(mem.TargetMat.Type)) {
                    agent.BB.SetGoalCooldown(goal.Id, G.Ticks,
                        Consts.TicksPerHour, Consts.TicksPer2Hours);
                    return FailedSoft(isGrounded
                        ? T.NavFailUnreachableGrounded : T.NavFailUnreachableFlying);
                }
                if (!mustMoveAside) {
                    return Failed(T.StorageUnavailable);
                }
            }

            mem.ReservedAmt = mem.TargetMat.Reserve(mem.TargetMatType,
                worker, toReserve, null);
            if (mem.ReservedAmt == 0) {
                goal.Flags |= AIGoalFlags.NonRetryable;
                return Failed(T.AdRejCouldNotReserve);
            }

            var matPos = goal.Target.Entity.PosIdx;
            if (worker.PosIdx != matPos) {
                agent.BB.MoveTargetPos = matPos;
                agent.Motion.TryGoTo(matPos,
                    PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                    out agent.BB.MoveProgress);
            }
            mem.Phase = Phase.PickUp;
            return InProgress;
        }

        private AIActionResult SeekMatExtra(AIGoal goal, AIAgentComp agent, Memory mem) {
            var worker = agent.Being;
            var carrying = worker.Graphics.Carrying;
            if (carrying == null) {
                D.Err("Trying to seek more mat but not carrying anything!");
                return Failed(T.AdRejTargetUnavailable);
            }


            var maxCarry = agent.SpareCarryAmount;
            if (maxCarry == 0 && agent.Being.Graphics.Carrying == null) {
                D.Err("Being has zero carry capacity: {0}!", agent);
            }
            if (maxCarry < 1) {
                // can't carry any more
                mem.Phase = Phase.MoveToStorage;
                return InProgress;
            }
            if (G.Ticks - mem.FirstPickupTick >= MaxCollectTicks) {
                mem.Phase = Phase.MoveToStorage;
                return InProgress;
            }

            // this can be expensive, let's only do this if we have the frame budget
            if (!GameTimer.HasAvailableFrameBudget()) {
                if (Rng.UChance(0.95f)) {
                    // think a bit longer
                    return InProgress;
                }
            }

            // need to find closest material piles that have a hauling task on them and
            // that are not reserved: of the same type, or of any type the planned
            // storage accepts
            var s = worker.S;
            var wp = worker.PosIdx;
            var origin = mem.ChainOrigin != Pos.Invalid ? mem.ChainOrigin : wp;
            const int maxDist = MaxExtraDist * MaxExtraDist;
            var aiPrecise = s.Prefs.GetBool(Pref.AIPrecision, PrefH.AIPrecision, true);
            if (aiPrecise) {
                // we are disassembling a derelict, let's not use precise AI
                if (!EntityUtils.IsWithinMargin(worker.PosIdx, false, false)) {
                    aiPrecise = false;
                }
            }
            if (aiPrecise) {
                // we are deconstructing something in space and hauling it?
                if (EntityUtils.IsEmptySpaceAt(worker.PosIdx)) {
                    aiPrecise = false;
                }
            }
            var primaryType = carrying is Obj co && co.Pile != null
                ? co.Pile.Type : mem.CarriedMat.Type;
            var planned = mem.PlannedStorage;
            if (planned != null && !planned.Entity.IsActive) {
                planned = mem.PlannedStorage = null;
            }
            var carriedTotal = worker.Graphics.CarryCtrl?.TotalCarriedStackSize ?? 0;
            // The region distance cap is applied by the pile search in one pass, not by
            // a path search per pile in the check: there can be hundreds of piles in reach.
            // Away from precise AI (a derelict at the map edge, open space) piles are ranked
            // by straight distance, which used to run an uncapped region search instead
            var extraPile = s.Sys.Inventory.FindClosestPileNear(
                worker, origin, MaxExtraDist, mem.IgnoredPiles, (pile) => {
                    // rule out direct dist
                    var pilePos = pile.Obj.PosIdx;
                    var sqrDist = Pos.SqrDistance(origin, pilePos);
                    if (sqrDist > maxDist) { return false; }
                    if (pile.TreatAsObj) { return false; }
                    if (pile.IsForbidden) { return false; }
                    if (pile.Type != primaryType) {
                        // Needs room for more than the load already carried, or the
                        // extra would only be dropped next to the storage
                        if (planned == null
                                || !planned.WillAccept(pile.Type, carriedTotal + 1, out _)) {
                            return false;
                        }
                    }
                    if (pile.UnreservedCount != pile.StackSize) { return false; }
                    var pileGoal = pile.Entity.Goal;
                    if (pileGoal != null) {
                        if (pileGoal.Agent != null) { return false; }
                        if (pileGoal.Target is not UnstoredMatComp) { return false; }
                    }
                    return true;
                }, aiPrecise ? 10 : -1, aiPrecise);
            if (extraPile == null || !extraPile.Entity.IsActive) {
                mem.Phase = Phase.MoveToStorage;
                return InProgress;
            }
            mem.TargetMat = (UnstoredMatComp) extraPile;
            mem.TargetMatType = mem.TargetMat.Type;
            if (mem.TargetMat.IsFullyReserved) {
                if (mem.TargetMat.HasReservation(worker)) {
                    D.Err("Unexpected reservation on a mat {0}", goal);
                    return Failed(T.AdRejUndefined);
                }
                mem.TargetMat = null;
                mem.Phase = Phase.SeekMatExtra;
                return InProgress;
            }

            var toReserve = Maths.Min(maxCarry, mem.TargetMat.StackSize);
            if (mem.TargetMatType != primaryType && planned is MatStorageComp plannedMs) {
                // Take only what still fits next to the load already carried
                toReserve = Maths.Min(toReserve, plannedMs.AvailableCapacity - carriedTotal);
                if (toReserve < 1) {
                    (mem.IgnoredPiles ??= new()).Add(mem.TargetMat);
                    mem.TargetMat = null;
                    mem.Phase = Phase.SeekMatExtra;
                    return InProgress;
                }
            }
            mem.ReservedAmt = mem.TargetMat.Reserve(
                mem.TargetMatType, worker, toReserve, null);
            if (mem.ReservedAmt == 0) {
                mem.TargetMat = null;
                mem.Phase = Phase.SeekMatExtra;
                return InProgress;
            }

            // The goal is taken away so no other hauler walks here for a pile that is
            // already spoken for, but only when nothing will be left for it: a partly
            // taken pile keeps its goal for another hauler to finish. Cancelling also
            // forbids hauling the pile (UnstoredMatComp.OnGoalChange), which is undone
            // here - it was not forbidden, or it would not have been picked
            mem.CancelledTargetGoal = false;
            if (extraPile.Entity.Goal != null
                    && mem.ReservedAmt >= mem.TargetMat.StackSize) {
                AIGoal.Cancel(extraPile.Entity.Goal, G.Ticks, T.HaulingToStorage);
                mem.TargetMat.IsForbidden = false;
                mem.CancelledTargetGoal = true;
            }

            var matPos = mem.TargetMat.OutPort;
            if (worker.PosIdx != matPos) {
                agent.BB.MoveTargetPos = matPos;
                agent.Motion.TryGoTo(matPos,
                    PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                    out agent.BB.MoveProgress);
            }
            mem.Phase = Phase.PickUp;
            return InProgress;
        }

        private AIActionResult PickUp(AIGoal goal, AIAgentComp agent, Memory mem) {
            var worker = agent.Being;
            if (mem.TargetMat == null) {
                D.Err("Cannot pick up mat for storing, it is null! w:{0} -> m: {1} {2}",
                    worker.Position, goal.Target.Entity.Position,
                    goal.Target);
                return Failed(T.AdRejTargetUnavailable);
            }
            var matPos = mem.TargetMat.OutPort;
            var sqrDist = Pos.SqrDistance(matPos, worker.PosIdx);
            if (sqrDist > 3) {
                if (agent.S.Sys.Ship.IsMoving && EntityUtils.IsEmptySpaceAt(matPos)) {
                    // try again
                    D.Warn("Retargeting floating pile: {0}->{1}",
                        Pos.StringFromTo(worker.PosIdx, matPos), goal);
                    agent.BB.MoveTargetPos = matPos;
                    if (agent.Motion.TryGoTo(matPos,
                            PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress)) {
                        return InProgress;
                    }
                } else {
                    agent.BB.RemoveActiveReservation("Too far from target mat");
                    agent.Motion.StoreNavFailureAt(mem.TargetMat.OutPort);
                }
                if (agent.Being.Graphics.IsCarrying()) {
                    // failed to get extra mat, try another
                    (mem.IgnoredPiles ??= new()).Add(mem.TargetMat);
                    GiveUpExtra(agent, mem, "Too far from extra mat");
                    mem.Phase = Phase.SeekMatExtra;
                    return InProgress;
                } else {
                    // this was the first mat, if we failed to reach it, let's fail the task
                    return FailedNavToMat(agent, matPos);
                }
            }

            var took = mem.TargetMat.Take(mem.TargetMatType,
                worker, mem.ReservedAmt, null);
            mem.ReservedAmt = 0;
            if (mem.TargetMat.StackSize < 1) {
                goal.Flags |= AIGoalFlags.NonRetryable;
                goal.Flags |= AIGoalFlags.NoTarget;
                //ad.NotReturnable("Stack exhausted");
            }
            if (took == 0) {
                if (mem.IsPickingUpExtra && mem.CarriedMat != null) {
                    // Losing an extra pile must not drop the whole load
                    (mem.IgnoredPiles ??= new()).Add(mem.TargetMat);
                    GiveUpExtra(agent, mem, "Could not take extra mat");
                    mem.Phase = Phase.SeekMatExtra;
                    return InProgress;
                }
                return Failed(T.AdRejTargetUnavailable);
            }

            if (mem.IsPickingUpExtra) {
                if (mem.CarriedMat == null) {
                    D.Err("Expected to be picking up extra material, but carried mat is null! {0}->{1}",
                        goal, agent);
                } else {
                    mem.CancelledTargetGoal = false;
                    AddExtraToLoad(worker, mem, took, goal);
                    if (agent.SpareCarryAmount > 0) {
                        mem.Phase = Phase.SeekMatExtra;
                    } else {
                        mem.Phase = Phase.MoveToStorage;
                    }
                    return InProgress;
                }
            }

            // Take just a fraction
            mem.CarriedMat = EntityUtils.SpawnRawMaterial(mem.TargetMatType,
                took, worker.PosIdx, 0, true, false);
            mem.FirstPickupTick = G.Ticks;
            goal.ReattachTo(mem.CarriedMat);
            //ad.WriteHistory(S, "Carrying fraction of mat", carriedMat.EntityId);
            //targetMat = carriedMat;
            if (mem.TargetMat.UnreservedCount > 0) {
                if (!mem.IsPickingUpExtra && !mem.TargetMat.HasAnyGoal) {
                    var deliverRestOfThePileGoal = mem.TargetMat.CreateStoringGoal(false, true);
                    if (deliverRestOfThePileGoal != null) {
                        mem.TargetMat.Entity.TrySetGoal(deliverRestOfThePileGoal);
                    }
                }
            } else {
                mem.TargetMat.HideAllInfo();
            }

            // We remove it since we don't want to reserve
            // mem.CarriedMat.RemoveFromInventory();
            // no we don't remove anymore, because it reduces the mat count in charts
            D.Ass(mem.CarriedMat.ReservationCount == 0,
                "Trying to carry mat with reservations! {0} {1}",
                mem.CarriedMat, null);
            if (!worker.Graphics.Carry(mem.CarriedMat.Obj, true, false)) {
                D.Err("Failed carrying stored mat: {0} {1}", mem.CarriedMat, this);
                return Failed(T.AdRejTargetUnavailable);
            }

            LoadCompatibleStorages(mem, worker, mem.CarriedMat.Type, mem.CarriedMat.StackSize);
            if (mem.CompatibleStorages.Count == 0) {
                mem.Phase = Phase.MoveAside;
            } else {
                mem.PlannedStorage = ClosestFrom(worker, mem.CompatibleStorages);
                mem.ChainOrigin = matPos;
                mem.IsPickingUpExtra = true;
                if (agent.SpareCarryAmount > 0) {
                    mem.Phase = Phase.SeekMatExtra;
                } else {
                    mem.Phase = Phase.MoveToStorage;
                }
            }

            return InProgress;
        }
        private AIActionResult MoveAside(AIGoal goal, AIAgentComp agent, Memory mem) {
            var worker = agent.Being;

            var foundPos = agent.S.Query.FindNearby.Ask(worker.PosIdx,
                    agent.Motion.IsGrounded, 1, (p) => {
                if (p == agent.PosIdx) {
                    return false;
                }
                if (EntityUtils.IsEmptyFloorAt(p, worker.Motion.IsFlying)) {
                    agent.BB.MoveTargetPos = p;
                    if (worker.Motion.TryGoTo(p,
                            PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby,
                            out agent.BB.MoveProgress)) {
                        return true;
                    }
                }
                return false;
            });

            if (foundPos == Pos.Invalid) {
                return Failed(UnreachableBy(agent));
            }

            // We couldn't find a storage
            if (worker.Motion.IsFlying) {
                mem.TargetMat.NeedsFurtherHauling = false;
            }

            mem.Phase = Phase.Done;
            return InProgress;
        }

        private AIActionResult LoadCollectedMat(AIGoal goal, AIAgentComp agent,
                Memory mem) {
            var worker = agent.Being;
            if (mem.CarriedMat == null) {
                D.Err("Carried material is null!");
                return Failed(T.AdRejUndefined);
            }
            if (!mem.CarriedMat.Entity.IsActive) {
                return Failed(T.AdRejTargetUnavailable);
            }
            D.Ass(mem.CarriedMat.Entity == worker.Graphics.Carrying,
                "Carrying something unexpected! {0} != {1}, {2}",
                mem.CarriedMat.Entity, worker.Graphics.Carrying, goal);
            var sqrDist = Pos.SqrDistance(mem.CarriedMat.OutPort, worker.PosIdx);
            if (sqrDist > 2) {
                var dist = math.sqrt(sqrDist);
                D.Err("Carried mat is too far away from worker! {0} {1}",
                    mem.CarriedMat.Entity, this,
                        Pos.StringFromTo(worker.PosIdx, mem.CarriedMat.OutPort),
                        dist);
                #if !PRODUCTION_BUILD
                EntityUtils.CameraFocusOn(mem.CarriedMat.Entity);
                worker.S.Clock.SetSpeed(ClockSpeed.Stopped);
                #endif
            }
            var amt = mem.CarriedMat.StackSize;
            if (mem.SeekedStorage == null) {
                D.Err("Sought storage is null! {0}", goal);
                worker.Graphics.Drop(mem.CarriedMat.Obj);
                return Failed(T.AdRejUndefined);
            }
            var hasExtras = !Arrays.IsEmpty(mem.ExtraMats);
            if (hasExtras) {
                // Unchain the extras first: storing can exhaust any carried pile, and a
                // destroyed pile in the middle of the sprite chain would orphan the rest
                worker.Graphics.CarryCtrl?.DropAllAdditional(skipInventoryCheck: true);
            }
            var excess = mem.SeekedStorage.Store(
                mem.CarriedMat.Type, worker, amt, null);
            var actAmt = amt - excess;

            if (actAmt > 0) {
                Mat.PlayProvideVFX(worker.S, worker.Position,
                    mem.SeekedStorage.Entity.Position, actAmt);
                mem.CarriedMat.ReserveAndTake(
                    mem.CarriedMat.Type, worker, actAmt, null);
            }
            if (hasExtras) {
                StoreExtras(worker, mem);
            }
            if (mem.CarriedMat.StackSize > 0) {
                // We didn't manage to put the whole pile in the storage
                // let's return to move to storage phase
                mem.Phase = Phase.MoveToStorage;
                mem.SeekedStorage = null;
                return InProgress;
            }
            // why?
            // carriedMat.ReaddToInventoryIfNeeded();
            worker.Graphics.ClearCarrying();
            mem.CarriedMat = null;
            mem.Phase = Phase.Done;
            return InProgress;
        }

        private AIActionResult MoveToStorage(AIGoal goal, AIAgentComp agent, Memory mem) {
            var worker = agent.Being;
            LoadCompatibleStorages(mem, worker, mem.CarriedMat.Type, mem.CarriedMat.StackSize);
            if (mem.SeekedStorage == null ||
                    Pos.SqrDistance(mem.SeekedStorage.InPort, worker.PosIdx) > 1) {
                if (Arrays.IsEmpty(mem.CompatibleStorages)) {
                    // Nowhere to store, let's move aside
                    mem.Phase = Phase.MoveAside;
                    return InProgress;
                }
                // The extras of other types were picked up for the planned storage
                var planned = mem.PlannedStorage;
                mem.PlannedStorage = null;
                if (planned != null && planned.Entity.IsActive
                        && mem.CompatibleStorages.Contains(planned)) {
                    mem.SeekedStorage = planned;
                } else {
                    mem.SeekedStorage = ClosestFrom(worker, mem.CompatibleStorages);
                }
                if (mem.SeekedStorage == null) {
                    return Failed($"{T.AdRejUnreachable}: {worker.Data.Mind.LinkToShortName} {GlyphMap.ArrowRight} {T.Storage}");
                }
                mem.CompatibleStorages.Remove(mem.SeekedStorage);
                if (mem.SeekedStorage.InPort != worker.PosIdx) {
                    agent.BB.MoveTargetPos = mem.SeekedStorage.InPort;
                    if (!worker.Motion.TryGoTo(agent.BB.MoveTargetPos,
                            PathReqFlags.AdjOnTop | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress)) {
                        agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                        mem.SeekedStorage = null;
                        agent.BB.ClearMoveTarget();
                    }
                }
                return InProgress;
            }

            // We are at the storage
            mem.Phase = Phase.LoadCollectedMat;
            return InProgress;
        }

        private static void LoadCompatibleStorages(Memory mem, Being worker, MatType type,
                int amt) {
            if (mem.CompatibleStorages == null) {
                mem.CompatibleStorages = worker.S.Sys.Inventory
                    .FindCompatibleStorages(worker, type, amt, forTaking: false);
            }
        }

        private static void AddExtraToLoad(Being worker, Memory mem, int took, AIGoal goal) {
            var type = mem.TargetMatType;
            var carried = type == mem.CarriedMat.Type ? mem.CarriedMat : mem.FindExtra(type);
            if (carried != null) {
                carried.Store(type, worker, took, goal);
                return;
            }
            var pile = EntityUtils.SpawnRawMaterial(type, took, worker.PosIdx, 0, true, false);
            if (pile == null) {
                D.Err("Failed spawning carried extra mat {0} x{1}: {2}", type, took, goal);
                return;
            }
            if (worker.Graphics.CarryCtrl?.CarryAdditional(pile.Obj) == true) {
                (mem.ExtraMats ??= new()).Add(pile);
            } else {
                pile?.OrderStoreIfLoose();
            }
        }

        // Releases an extra pile the worker won't pick up after all, and gives back the
        // goal SeekMatExtra took from it
        private static void GiveUpExtra(AIAgentComp agent, Memory mem, string reason) {
            agent.BB.RemoveActiveReservation(reason);
            mem.ReservedAmt = 0;
            mem.ReorderCancelledTarget();
        }

        private static void StoreExtras(Being worker, Memory mem) {
            var storage = mem.SeekedStorage;
            var isStorageActive = storage.Entity.IsActive;
            for (int i = 0; i < mem.ExtraMats.Count; i++) {
                var extra = mem.ExtraMats[i];
                if (!extra.Entity.IsActive || extra.StackSize < 1) { continue; }
                var type = extra.Type;
                // Store() does not check the filter, and the storage may have changed
                // its mind since the extra was picked up
                if (isStorageActive && storage.WillAccept(type, 1, out _)) {
                    var amt = extra.StackSize;
                    var actAmt = amt - storage.Store(type, worker, amt, null);
                    if (actAmt > 0) {
                        Mat.PlayProvideVFX(worker.S, worker.Position,
                            storage.Entity.Position, actAmt);
                        extra.ReserveAndTake(type, worker, actAmt, null);
                    }
                }
                if (extra.Entity.IsActive && extra.StackSize > 0) {
                    // Leave the rest here, already inside the ship, for another trip
                    extra.UpdateForbiddenIcon(true);
                    extra.OrderStoreIfLoose();
                }
            }
            mem.ExtraMats.Clear();
        }

        private IMatStorage ClosestFrom(Being worker, List<IMatStorage> storages) {
            if (Arrays.IsEmpty(storages)) { return null; }
            var wp = worker.PosIdx;
            IMatStorage closest = null;
            int bestPriority = 0;
            float bestDist = float.MaxValue;
            for (int i = storages.Count - 1; i >= 0; i--) {
                var st = storages[i];
                if (st.Priority == 0) { continue; }
                if (worker.Motion.HadNavFailureAt(st.InPort)) {
                    continue;
                }
                float dist = Pos.SqrDistance(wp, st.InPort);
                if (EntityUtils.IsBetterStorage(st.Priority, dist, bestPriority, bestDist)) {
                    closest = st;
                    bestPriority = st.Priority;
                    bestDist = dist;
                }
            }
            return closest;
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
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 3f;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveProgress = null;
            agent.BB.RemoveActiveReservation(goal);
            if (agent.ActionMemory is Memory mem) {
                mem.Cleanup(agent);
            }
        }
    }
}
