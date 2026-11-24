using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Utils;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.Creatures.Surgeries {
    /// <summary>
    /// TODO:
    /// - [ ] allow ordering installation of parts out of order, wait until operation can be performed
    /// </summary>
    public sealed class SurgicalQueue {
        internal readonly GameState S;
        internal readonly List<SurgicalOperation> allOperations = new();
        internal int lastSurgicalOperationId;

        public SurgicalQueue(GameState state) {
            S = state;
            S.Query.RegisterSurgicalOperation.SetResponder(RegisterOp);
            S.Query.FindSurgicalOperation.SetResponder(FindOp);
            S.Query.GetSurgicalOperationsFor.SetResponder(GetOpsFor);
            S.Query.ClearSurgicalOpsFor.SetResponder(ClearOpsFor);
            S.Query.HasPendingSurgeries.SetResponder(HasSurgeries);
            S.Query.GetSurgicalOp.SetResponder(GetOp);
            S.Query.TryNextSurgicalProcedure.SetResponder(TryNextSurgery);
            S.Query.GetOngoingSurgicalOp.SetResponder(GetOngoingOp);
            S.Query.CanEnqueueMassSurgicalOp.SetResponder(CanEnqueueMassOp);
            S.Query.GetAllSurgeries.SetResponder(() => allOperations);
        }

        private SurgicalOperation GetOngoingOp(BeingData being) {
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing == being
                        && (op.State.Value == SurgicalOpState.Preparing
                        || op.State.Value == SurgicalOpState.Ongoing)) {
                    return op;
                }
            }
            return null;
        }
        private SurgicalOperation GetOp(int opId) {
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.Id == opId) {
                    return op;
                }
            }
            return null;
        }

        private bool CanEnqueueMassOp(BeingData being, SurgicalOpType type) {
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing == being) {
                    // not a mass op
                    if (op.TargetSlot == null) {
                        // already has mass op
                        return false;
                    }
                }
            }
            return true;
        }

        private bool HasSurgeries(BeingData being, BodyPartSlot slot) {
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing == being) {
                    if (slot == null) { return true; }
                    if (op.TargetSlot == slot || op.TargetSlot == null) {
                        return true;
                    }
                }
            }
            return false;
        }

        public void Tick(long ticks) {
            if (ticks % Consts.TicksPer10Minutes != 2) { return; }
            if (allOperations.Count == 0) { return; }
            // before the free hub check: a busy hub must not keep a raider's queue alive
            DropOpsOnAwakeHostiles();
            if (allOperations.Count == 0) { return; }
            var hasBio = S.Sys.Slots.HasFreeFor(SlotDesignationH.BodyModSpot_Organic);
            var hasRobo = S.Sys.Slots.HasFreeFor(SlotDesignationH.BodyModSpot_Robotic);
            if (!hasBio && !hasRobo) { return; }
            var candidates = Caches.BeingDataList;
            candidates.Clear();
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (!op.IsActive) {
                    if (op.State.Value == SurgicalOpState.Failed) {
                        // e.g. its part was gone when the game was loaded
                        DropFailedOp(op);
                        continue;
                    }
                    D.Err("Sanity check: neither pending nor preparing: {0}", op);
                    allOperations.RemoveAt(i);
                    S.Sig.SurgicalOperationRegistered.Send(op, false);
                    continue;
                }
                if (op.State.Value == SurgicalOpState.Ongoing) {
                    if (!op.TargetBeing.HasStateFlag(BeingState.InDevice)) {
                        D.Err("Being is not in device but has ongoing surgery: {0}", op);
                        allOperations.RemoveAt(i);
                        S.Sig.SurgicalOperationRegistered.Send(op, false);
                    }
                    continue;
                }
                if (op.State.Value == SurgicalOpState.Preparing) {
                    if (AIGoal.IsActive(op.AssociatedGoal)) {
                        continue;
                    }
                }
                var being = op.TargetBeing;
                if (being.Species.IsBiological) {
                    if (!hasBio) { continue; }
                } else {
                    if (!hasRobo) { continue; }
                }
                if (being.HasStateFlag(BeingState.Expedition)) {
                    continue;
                }
                if (being.HasStateFlag(BeingState.Stored)) {
                    var storage = S.Components.Find<BeingStorageComp>(being.StorageId);
                    if (!S.Query.UnstoreBeing.Ask(being, storage)) {
                        D.Err("Failed to unstore a stored being for surgery: {0}", being);
                    }
                    continue;
                }
                if (being.Being == null) {
                    // expedition or something, normal
                    if (being.Body.HasFlag(BodyFlags.Missing)) {
                        D.Err("Being body is missing while waiting for surgery. Cancelling: {0}", being, op);
                        RegisterOp(op, false);
                    }
                    continue;
                }
                if (being.Being.Goal != null) {
                    continue;
                }
                candidates.AddUniqueInefficient(op.TargetBeing);
            }

            for (int i = candidates.Count - 1; i >= 0; i--) {
                BeingData being = candidates[i];
                if (TryOrderSurgery(being) != null) {
                    break;
                }
            }
        }


        // A raider who came to before reaching the table: the queue would have a medic try to
        // carry a conscious enemy. One already on the table is the hub's (BodyModSpotComp).
        private void DropOpsOnAwakeHostiles() {
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                // ClearOpsFor removes the being's other ops too
                if (i >= allOperations.Count) { continue; }
                var being = allOperations[i].TargetBeing;
                if (being.HasStateFlag(BeingState.InDevice)) { continue; }
                if (!SurgeryUtils.IsAwakeHostilePatient(being)) { continue; }
                ClearOpsFor(being);
                S.Sys.Log.AddLine(being, T.SurgeryPatientCameTo);
            }
        }

        private SurgicalOperation TryOrderSurgery(BeingData being) {
            SurgicalOperation nextOp = TryNextSurgery(being);
            // no next op, process next being
            if (nextOp == null) { return null; }
            D.Warn("Will order op for being: {0} -> {1}", being, nextOp);
            if (!being.Being.TryGetComponent<BodyComp>(out var body)) {
                D.Err("Being has no BodyComp!? {0}", being);
                return null;
            }
            if (body.TryOrderOp(nextOp)) {
                nextOp.State.Set(SurgicalOpState.Preparing);
                D.Warn("Ordered op for being: {0}", nextOp);
                // let's order only one op per tick
                return nextOp;
            } else if (!nextOp.IsActive) {
                DropFailedOp(nextOp);
            } else {
                D.Warn("Failed to order op for being: {0}", nextOp);
            }

            return null;
        }

        // An op that can never be performed leaves the queue once, and the player learns why,
        // instead of being retried every few minutes forever (STA-1584)
        private void DropFailedOp(SurgicalOperation op) {
            RegisterOp(op, false);
            S.Sys.Log.AddLine(op.TargetBeing, $"{op.Desc}: {op.FailReason ?? T.OperationFailed}");
        }

        private SurgicalOperation TryNextSurgery(BeingData being) {
            SurgicalOperation nextOp = null;
            var cnt = allOperations.Count;
            for (int i = 0; i < cnt; i++) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing != being) { continue; }
                if (op.State.Value == SurgicalOpState.Ongoing) {
                    // next being, this one is in surgery already
                    nextOp = null;
                    break;
                }
                if (op.State.Value == SurgicalOpState.Preparing) {
                    if (AIGoal.IsActive(op.AssociatedGoal)) {
                        // go to next being
                        nextOp = null;
                        break;
                    }
                }
                if (op.TargetSlot == null) {
                    // mass op
                    nextOp = op;
                    break;
                }
                if (nextOp == null) {
                    nextOp = op;
                }
            }
            return nextOp;
        }
        private bool ClearOpsFor(BeingData being) {
            bool found = false;
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing == being) {
                    found = true;
                    op.State.Set(SurgicalOpState.Canceled);
                    RegisterOp(op, false);
                }
            }
            return found;
        }

        private int GetOpsFor(Body body, SurgicalOperation[] cache) {
            int found = 0;
            var owner = body.Owner;
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetBeing == owner) {
                    cache[found++] = op;
                    if (found >= cache.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        private SurgicalOperation FindOp(BodyPartSlot slot, bool includeMassOpsAffectingSlot) {
            SurgicalOperation massOp = null;
            var b = slot.Body.Owner;
            for (int i = allOperations.Count - 1; i >= 0; i--) {
                SurgicalOperation op = allOperations[i];
                if (op.TargetSlot == slot) {
                    return op;
                }
                if (!includeMassOpsAffectingSlot) { continue; }
                if (op.TargetBeing != b) { continue; }
                if (massOp == null && op.TargetSlot == null && AffectsSlot(op, slot)) {
                    massOp = op;
                }
            }
            return massOp;
        }

        private bool AffectsSlot(SurgicalOperation op, BodyPartSlot slot) {
            if (slot.Type.IsOptional) {
                if (op.OpType == SurgicalOpType.FillMissing) {
                    return false;
                }
            }
            if (!slot.Type.IsRemovable) {
                return false;
            }
            if (slot.IsDestroyed) {
                return false;
            }
            if (slot.State?.IsWelded == true) {
                return false;
            }
            // below a slot that can never work again a part only comes out (STA-1749)
            switch (op.OpType) {
                case SurgicalOpType.RemoveAll: {
                    return !slot.IsPartRemoved;
                }
                case SurgicalOpType.RemoveIrreparable: {
                    return !slot.IsPartRemoved && (slot.State.WoundSeverity >= Severity.Irreparable
                        || slot.BeyondRepairAncestor != null);
                }
                case SurgicalOpType.ReplaceIrreparableAndMissing: {
                    if (slot.IsPartRemoved) {
                        return slot.BeyondRepairAncestor == null;
                    }
                    return slot.State.WoundSeverity >= Severity.Irreparable
                        || slot.BeyondRepairAncestor != null;
                }
                case SurgicalOpType.FillMissing: {
                    return slot.IsPartRemoved && slot.BeyondRepairAncestor == null;
                }
            }
            return false;
        }

        private bool RegisterOp(SurgicalOperation op, bool add) {
            if (add) {
                if (ValidateAdd(op)) {
                    op.CreatedAt = G.Ticks;
                    op.State.Set(SurgicalOpState.Pending);
                    op.UpdateDesc();
                    allOperations.Add(op);
                    S.Sig.SurgicalOperationRegistered.Send(op, true);
                    return true;
                }
                return false;
            } else {
                if (ValidateRemove(op)) {
                    allOperations.Remove(op);
                    S.Sig.SurgicalOperationRegistered.Send(op, false);
                    op.PostRemove();
                    return true;
                }
                return false;
            }
        }


        private bool ValidateAdd(SurgicalOperation op) {
            if (!op.Validate(true)) {
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            if (!ValidateFacilities(op)) {
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            if (op.ReservedPart != null && op.ReservedPart.Reservation?.Surgery != op) {
                // will reserve automatically
                D.Warn("Adding surgical op without part reservation, reserving part now: {0}", op);
                if (!op.ReservedPart.TryReserveForOperation(op)) {
                    return false;
                }
            }
            if (allOperations.Contains(op)) {
                D.Err("Trying to add a duplicate surgical op: {0}", op);
                return false;
            }
            return true;
        }

        private bool ValidateFacilities(SurgicalOperation op) {
            if (!op.TargetBeing.Body.HasSurgeryFacilities(out var defIdH)) {
                op.FailReason = $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(defIdH)}";
                return false;
            }
            return true;
        }

        private bool ValidateRemove(SurgicalOperation op) {
            if (op.State.Value == SurgicalOpState.Ongoing && op.TargetSlot != null) {
                op.FailReason = $"{T.OperationForbidden}: {T.AdjOngoing}";
                return false;
            }
            return true;
        }
    }
}
