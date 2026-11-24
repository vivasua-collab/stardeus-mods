using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Creatures.Surgeries {
    [MessagePackObject]
    public class SurgicalQueueData {
        [Key("LastSurgicalOperationId")] public int LastSurgicalOperationId;
        [Key("Operations")] public SurgicalOperationData[] Operations;

        public static SurgicalQueueData Serialize(SurgicalQueue queue) {
            if (queue == null) { return null; }

            return new SurgicalQueueData {
                LastSurgicalOperationId = queue.lastSurgicalOperationId,
                Operations = SurgicalOperationData.Serialize(queue.allOperations)
            };
        }

        public static void Deserialize(SurgicalQueueData data, SurgicalQueue queue, Dictionary<uint, BeingData> allBeings) {
            if (data == null || queue == null) {
                return;
            }

            queue.lastSurgicalOperationId = data.LastSurgicalOperationId;

            if (data.Operations != null) {
                queue.allOperations.Clear();
                foreach (var opData in data.Operations) {
                    if (SurgicalOperationData.TryDeserialize(queue.S, opData, allBeings, out var op)) {
                        queue.allOperations.Add(op);
                    }
                }
            }
        }
    }

    [MessagePackObject]
    public class SurgicalOperationData {
        [Key("Id")] public int Id;
        [Key("TargetBeingId")] public uint TargetBeingId;
        [Key("TargetSlotId")] public int TargetSlotId;
        [Key("OpType")] public SurgicalOpType OpType;
        [Key("ReservedPartId")] public uint ReservedPartId;
        [Key("State")] public SurgicalOpState State;
        [Key("Icon")] public string Icon;
        [Key("Desc")] public string Desc;
        [Key("FailReason")] public string FailReason;
        [Key("AssociatedGoalId")] public uint AssociatedGoalId;
        [Key("CreatedAt")] public long CreatedAt;
        [Key("RestrictedOperatorMindIds")] public int[] RestrictedOperatorMindIds;

        public static SurgicalOperationData[] Serialize(List<SurgicalOperation> operations) {
            if (operations == null || operations.Count == 0) {
                return null;
            }

            var data = new SurgicalOperationData[operations.Count];
            for (int i = 0; i < operations.Count; i++) {
                data[i] = Serialize(operations[i]);
            }
            return data;
        }

        public static SurgicalOperationData Serialize(SurgicalOperation op) {
            if (op == null) { return null; }

            return new SurgicalOperationData {
                Id = op.Id,
                TargetBeingId = op.TargetBeing?.Id ?? 0,
                TargetSlotId = op.TargetSlot?.IdH ?? 0,
                OpType = op.OpType,
                ReservedPartId = op.ReservedPart?.Id ?? 0,
                State = op.State.Value,
                Icon = op.Icon,
                Desc = op.Desc,
                FailReason = op.FailReason,
                AssociatedGoalId = op.AssociatedGoal?.Id ?? 0,
                CreatedAt = op.CreatedAt,
                RestrictedOperatorMindIds = op.RestrictedOperatorMindIds == null
                    ? null
                    : Array.ConvertAll(op.RestrictedOperatorMindIds, id => (int) id)
            };
        }

        public static bool TryDeserialize(GameState S, SurgicalOperationData data, Dictionary<uint, BeingData> allBeings, out SurgicalOperation op) {
            op = null;
            if (data == null) {
                return false;
            }

            // Find the target being
            if (data.TargetBeingId == 0 || !allBeings.TryGetValue(data.TargetBeingId, out var targetBeing)) {
                D.Err("Failed to deserialize surgical operation: target being not found. BeingId: {0}", data.TargetBeingId);
                return false;
            }

            // Create the operation
            op = new SurgicalOperation {
                TargetBeing = targetBeing,
                OpType = data.OpType,
                Icon = data.Icon,
                Desc = data.Desc,
                FailReason = data.FailReason,
                CreatedAt = data.CreatedAt
            };
            op.State.SetSilent(data.State);

            // Find the target slot if specified
            if (data.TargetSlotId != 0) {
                if (!targetBeing.Body.TryGetSlot(data.TargetSlotId, out var targetSlot)) {
                    D.Warn("Failed to find target slot for surgical operation: {0}. Slot may have been removed.", data.TargetSlotId);
                    // Don't fail completely, mass operations don't need specific slots
                }
                op.TargetSlot = targetSlot;
            }

            // Find the reserved part if specified
            if (data.ReservedPartId != 0) {
                var reservedPart = S.Query.FindObjData.Ask(data.ReservedPartId);
                if (reservedPart != null) {
                    op.ReservedPart = reservedPart;
                    // Restore the reservation link
                    if (reservedPart.Reservation?.Surgery == null) {
                        reservedPart.Reservation ??= new ObjReservation(reservedPart, data.CreatedAt);
                        reservedPart.Reservation.Surgery = op;
                    }
                } else {
                    D.Warn("Failed to find reserved part for surgical operation: {0}. Part may have been destroyed.", data.ReservedPartId);
                    op.FailReason = T.AdRejTargetUnavailable;
                    op.State.SetSilent(SurgicalOpState.Failed);
                    // Don't fail completely, operation might still be valid without the part
                }
            }

            // Find the associated goal if specified (will be linked later during goal restoration)
            if (data.AssociatedGoalId != 0) {
                op.AssociatedGoal = S.Query.FindAIGoal.Ask(data.AssociatedGoalId, true);
            }

            if (data.RestrictedOperatorMindIds != null) {
                op.RestrictedOperatorMindIds = Array.ConvertAll(data.RestrictedOperatorMindIds, id => (uint) id);
            }

            return true;
        }
    }
}
