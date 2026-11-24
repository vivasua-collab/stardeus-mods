using Game.Components;
using Game.Data;
using Game.Systems.Path;
using KL.Grid;

namespace Game.Systems.AI {
    // A colonist's binge: take a stack of food (one portion for every bite still to
    // come), carry it to a table, put it down and eat it there portion by portion.
    // When the stack runs out before the binge does, go back for more.
    //
    // The phase is read from state that is not saved (Bool2NotSaved: the stack is
    // down, the carried item, MoveProgress), so after a load the being always starts
    // over at "find food". Its stack is back in the inventory by then, and usually
    // the closest food, so it picks it up again. Keep it that way when adding phases.
    public sealed partial class AIBreakdownActionFoodBinge {
        private AIActionResult ExecuteAtTable(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.BB.Bool2NotSaved) {
                return EatFromStack(S, agent, goal, ticks);
            }
            if (IsCarryingStack(agent)) {
                if (agent.BB.MoveProgress == null) {
                    return HeadForTable(S, agent, goal);
                }
                return ServeAtTable(agent);
            }
            // Int1NotSaved is 0 when no food is reserved: after a load, or when the
            // stack left our hands without us putting it down
            if (agent.BB.MoveProgress == null || agent.BB.Int1NotSaved == 0) {
                PutBackLeftovers(agent);
                ReleaseTarget(agent, goal);
                var portionsLeft = agent.BB.Int1 - agent.BB.Byte1;
                var capacity = agent.BeingData.StatsBeing.CarryCapacity.ValueInt;
                if (portionsLeft > capacity) { portionsLeft = capacity; }
                if (!TryHeadForFood(S, agent, goal, portionsLeft)) {
                    // The food stock is gone, or out of reach
                    return Finish(S, agent, goal, ticks);
                }
                return InProgress;
            }
            return PickUpStack(agent, goal);
        }

        private static bool IsCarryingStack(AIAgentComp agent) {
            var target = agent.BB.TargetEntity;
            return target != null && agent.Being.Graphics.Carrying == target;
        }

        // At the storage: take what was reserved, as one stack, and pick it up
        private AIActionResult PickUpStack(AIAgentComp agent, AIGoal goal) {
            var storage = StorageOf(agent.BB.TargetEntity);
            if (!IsNextTo(agent, storage)) {
                LeaveStorage(agent, goal, storage);
                return InProgress;
            }
            var type = agent.BB.TargetMatType;
            var reserved = agent.BB.Int1NotSaved;
            var took = reserved > 0 ? storage.Take(type, agent.Being, reserved, goal) : 0;
            ReleaseTarget(agent, goal);
            if (took < 1) {
                agent.BB.AddToBlacklist(storage.EntityId);
                return InProgress;
            }
            var stack = EntityUtils.SpawnRawMaterial(type, took, agent.PosIdx, 0f, true, false);
            if (stack == null) {
                return InProgress;
            }
            // Out of the inventory while it is ours, so nobody else takes it
            stack.RemoveFromInventory();
            agent.BB.TargetEntity = stack.Obj;
            agent.BB.TargetMatType = type;
            if (!agent.Being.Graphics.Carry(stack.Obj, false, false)) {
                // Could not pick it up: it lies at our feet, eat it right here
                agent.BB.Bool2NotSaved = true;
            }
            return InProgress;
        }

        // A table with a chair first, then any table, then a makeshift one
        private AIActionResult HeadForTable(GameState S, AIAgentComp agent,
                AIGoal goal) {
            agent.BB.UnreserveSlotSide(false);
            var slot = S.Sys.Slots.FindForFood(agent.Being, agent.BB.Blacklist,
                out var sideIdx);
            if (slot == null) {
                // No table, not even a makeshift one: eat right here
                agent.Being.Graphics.Drop(agent.BB.TargetEntity, true);
                agent.BB.Bool2NotSaved = true;
                return InProgress;
            }
            if (!agent.BB.TryReserveSlotSide(slot, sideIdx, goal)) {
                agent.BB.AddToBlacklist(slot.Parent.EntityId);
                return InProgress;
            }
            agent.BB.MoveTargetPos = sideIdx;
            if (!agent.Motion.TryGoTo(sideIdx, PathReqFlags.AdjOnTop,
                    out agent.BB.MoveProgress)) {
                agent.BB.UnreserveSlotSide(true);
                agent.Motion.StoreNavFailureAt(sideIdx);
                agent.BB.ClearMoveTarget();
            }
            return InProgress;
        }

        // At the table: the same arrival checks a regular meal has (AIActionFindTable.UseTable)
        private AIActionResult ServeAtTable(AIAgentComp agent) {
            var slot = agent.BB.ReservedSlot;
            var spotIdx = agent.BB.ReservedSideIdx;
            var pos = agent.PosIdx;
            var walkFailed = agent.BB.MoveProgress.HasFailed;
            agent.BB.ClearMoveTarget();
            if (slot == null) {
                return InProgress;
            }
            if (walkFailed || (pos != spotIdx && !Pos.AreAdjacent(pos, spotIdx))
                    || !agent.BeingData.Safety.IsActuallySafeAtWithoutEquipment(pos)) {
                if (walkFailed) {
                    agent.Motion.StoreNavFailureAt(spotIdx);
                }
                // find another table
                agent.BB.UnreserveSlotSide(true);
                return InProgress;
            }
            AIActionFindTable.ServeOnTable(agent.Being, slot, spotIdx, agent.BB.TargetEntity);
            agent.BB.Bool2NotSaved = true;
            return InProgress;
        }

        private AIActionResult EatFromStack(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var stack = (agent.BB.TargetEntity as Obj)?.Pile;
            if (stack == null || !stack.Entity.IsActive || stack.StackSize < 1
                    || stack.Entity.IsCarriedByAny
                    || Pos.SqrDistance(agent.PosIdx, stack.Entity.PosIdx) > 4) {
                // Eaten up, or out of reach: go back for more
                LeaveStack(agent, goal);
                return InProgress;
            }
            if (!EatAnimation(agent, stack.Entity.Position, ticks)) {
                return InProgress;
            }
            var type = stack.Type;
            // The stack is neither carried nor in the inventory, so it is taken from
            // without a worker
            if (stack.Take(type, null, 1, goal) > 0) {
                AtePortion(S, agent, type);
            } else {
                LeaveStack(agent, goal);
            }
            return InProgress;
        }

        private static void LeaveStack(AIAgentComp agent, AIGoal goal) {
            PutBackLeftovers(agent);
            StandUp(agent);
            agent.BB.Bool2NotSaved = false;
            agent.BB.UnreserveSlotSide(false);
            ReleaseTarget(agent, goal);
        }

        // Back onto the side tile, upright. Eating leans the being into the table
        // (MoveNearbyAndFace), which can put it on the table's own tile, where nothing
        // it throws up can land.
        private static void StandUp(AIAgentComp agent) {
            if (agent.BB.ReservedSlot == null) { return; }
            agent.Motion.TeleportTo(EntityUtils.CenterOf(agent.BB.ReservedSideIdx));
            agent.Being.Graphics.SetRotation(0f, true);
        }

        // Whatever was not eaten becomes regular food again
        private static void PutBackLeftovers(AIAgentComp agent) {
            var graphics = agent.Being.Graphics;
            if (graphics.Carrying is Obj carried) {
                carried.Pile?.ReaddToInventoryIfNeeded();
                graphics.Drop(carried);
            }
            if (agent.BB.TargetEntity is Obj target) {
                target.Pile?.ReaddToInventoryIfNeeded();
            }
        }
    }
}
