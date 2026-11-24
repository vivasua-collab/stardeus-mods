using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Game.Systems.Drama;
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public abstract class AIActionUseSlot : AIAction {
        protected AIActionUseSlot(string id, Func<string> nameT = null) : base(id, nameT) {
        }

        protected abstract string FailMsgSlotUnavailable { get; }
        protected abstract int SlotDesignation { get; }
        protected abstract bool CanBeMakeshift { get; }
        protected abstract int failReachCooldownFrom { get; }
        protected abstract int failReachCooldownTo { get; }
        protected virtual int SocialOpportunityId => 0;
        protected virtual int SocialOpportunityDuration => 0;
        // Slots passing this check are tried first, the rest only when none of them do
        protected virtual Func<ISlots, bool> PreferredSlotCheck => null;
        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // start using slot
            agent.BB.Bool1NotSaved = false;
            // slot pos idx we started using
            agent.BB.Int1 = 0;
            // device of the slot we started using
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
            // slot reservation can be passed on by another action
            // agent.BB.UnreserveSlot(false, false);
            agent.BB.ClearBlacklist();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Bool1NotSaved
                    && agent.BB.ReservedSlot != null && AIGoal.IsInterrupted(goal)) {
                OnEndUsingSlot(agent, goal, agent.BB.ReservedSlot, ticks);
            }
            // cleanup the bb
            agent.BB.UnreserveSlot(false, false);
            agent.BB.ClearBlacklist();
            agent.BB.RemoveOpportunity(agent.S);
            agent.BB.Bool1NotSaved = false;
            agent.BB.Int1 = 0;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        protected abstract bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal);

        public override void AfterLoad(AIAgentComp agent, AIGoal goal) {
            if (agent.BB.Int1 != 0) {
                var slot = FindSlotInUseAfterLoad(agent);
                if (slot != null) {
                    if (!agent.BB.TryReserveSlot(slot, goal)) {
                        D.Err("Failed to re-reserve the slot after load: {0} -> {1}", slot, goal);
                    // } else {
                    //     D.Warn("Restored slot after load: {0} -> {1}", slot, goal);
                    }
                }
            }
        }

        // Slot contents are not saved, so every being in a slot claims its own back here.
        // The approach tile alone is ambiguous: devices whose slot is on a port can share
        // one (two AutoDocs, STA-1592) and a makeshift toilet has several slots on it, so
        // the device is saved too. Saves from before that only have the tile, and there
        // a slot already claimed by another being is skipped.
        private Slot FindSlotInUseAfterLoad(AIAgentComp agent) {
            var being = agent.Being;
            var tile = agent.BB.TargetTile;
            if (tile != null && tile.TryGetComponent<ISlots>(out var device,
                    (d) => d.SlotDesignation == SlotDesignation)) {
                for (int i = 0; i < device.Slots.Count; i++) {
                    var s = device.Slots[i];
                    if (CanTakeBackAfterLoad(s, being)) {
                        return s;
                    }
                }
                return null;
            }
            var approachPos = agent.BB.Int1;
            return agent.S.Sys.Slots.FindSlot(SlotDesignation,
                (s) => ApproachPosFor(s) == approachPos && CanTakeBackAfterLoad(s, being));
        }

        private static bool CanTakeBackAfterLoad(Slot slot, Being being) {
            return (slot.ReservedBy == null || slot.ReservedBy == being)
                && (slot.ContainedBeing == null || slot.ContainedBeing == being);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) { return InProgress; }

            var resSlot = agent.BB.ReservedSlot;

            if (CheckDone(S, agent, goal)) {
                // GTFO from current loc
                if (resSlot != null) {
                    resSlot.Remove(agent.Being);
                    OnEndUsingSlot(agent, goal, resSlot, ticks);
                }

                TryWalkAway(agent);
                return Success;
            }

            int pos;
            var agentData = agent.BeingData;
            if (resSlot == null) {
                // find and reserve a slot
                ISlots slots = null;
                Slot slot = null;
                SocialOpportunity existingOP = null;
                if (SocialOpportunityId != 0) {
                    // try closest non-expired opportunity
                    existingOP = S.Query.FindSocialOpportunity.Ask(
                        agent.BeingData, SocialOpportunityId);
                }
                var reach = S.Sys.Path.Reg.Reach.For(agent.Motion);
                if (CanBeMakeshift) {
                    slot = S.Sys.Slots.FindForDesignation(
                        agentData, SlotDesignation, reach,
                        agent.BB.Blacklist, (s) => !s.IsMakeshift, out slots,
                            existingOP?.Slot);
                    if (slot == null) {
                        // fallback to makeshift
                        slot = S.Sys.Slots.FindForDesignation(
                            agentData, SlotDesignation, reach,
                            agent.BB.Blacklist, (s) => s.IsMakeshift, out slots);
                    }
                } else {
                    if (PreferredSlotCheck != null) {
                        slot = S.Sys.Slots.FindForDesignation(
                            agentData, SlotDesignation, reach,
                            agent.BB.Blacklist, PreferredSlotCheck, out slots,
                                existingOP?.Slot);
                    }
                    if (slot == null) {
                        slot = S.Sys.Slots.FindForDesignation(
                            agentData, SlotDesignation, reach,
                            agent.BB.Blacklist, null, out slots, existingOP?.Slot);
                    }
                }
                if (slot == null || !slots.IsAvailable) {
                    // D.Warn("Slot: {0}", slot);
                    return OnSlotUnavailable(agent, goal, ticks);
                }
                if (!agent.BB.TryReserveSlot(slot, goal)) {
                    // oh well, let's try another one
                    agent.BB.AddToBlacklist(slots.EntityId);
                } else {
                    pos = ApproachPosFor(slot);
                    agent.BB.MoveTargetPos = pos;
                    if (!agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.TempDestination,
                            out agent.BB.MoveProgress)) {
                        agent.BB.AddToBlacklist(slots.EntityId);
                        agent.BB.UnreserveSlot(true, false);
                    }
                }
                return InProgress;
            }

            pos = ApproachPosFor(resSlot);

            if (Pos.SqrDistance(agent.PosIdx, pos) < 2) {
                if (!agent.BB.Bool1NotSaved) {
                    if (resSlot.Put(agent.Being)) {
                        agent.BB.Bool1NotSaved = true;
                        agent.BB.Int1 = pos;
                        agent.BB.TargetTile = resSlot.Parent.Tile;
                        if (SocialOpportunityId != 0 && !resSlot.Parent.IsMakeshift) {
                            var op = SocialOpportunity.CreateFor(agent, resSlot,
                                SocialOpportunityId, SocialOpportunityDuration);
                            agent.BB.AddOpportunity(S, op);
                        }
                        return OnStartUsingSlot(agent, goal, resSlot, ticks);
                    } else {
                        return OnSlotUnavailable(agent, goal, ticks);
                    }
                }
                if (resSlot.Parent.IsUsable
                        && resSlot.Parent.Use(agentData, resSlot)) {
                    return OnUseSlot(agent, goal, resSlot, ticks);
                } else {
                    return OnUseSlotFail(agent, goal, resSlot, ticks);
                }
            } else {
                // somehow we are not at the slot yet, did we fail
                // the pathfinding?
                if (failReachCooldownTo > 0) {
                    // D.Warn("Failed to reach a slot, setting cooldown: {0} | {1} | {2} | {3} | {4}",
                    //     Id, failReachCooldownFrom, failReachCooldownTo, agent, goal);
                    agent.BB.SetCooldown(IdH, ticks, failReachCooldownFrom, failReachCooldownTo);
                }
                if (agent.BB.MoveTargetPos > 0) {
                    return FailedNavToTarget(agent);
                } else {
                    return FailedNavToTargetWithoutStore(agent);
                }
            }
        }
        // We can't go to slotPosIdx because it can be inside a device, so a slot is
        // approached via the in port or similar. A slot that sits on the work spot (Tube
        // Feeder, Pet Bowl) is used right there: its in port is the refill side, which can
        // be behind a wall (STA-1370, a feeder serving a cell from outside).
        private static int ApproachPosFor(Slot slot) {
            var tt = slot.Parent.Tile.Transform;
            if (tt.HasWorkSpot && slot.SlotPosIdx == tt.WorkSpot) {
                return tt.WorkSpot;
            }
            if (tt.HasInPort) {
                return tt.InPort;
            }
            if (tt.HasWorkSpot) {
                return tt.WorkSpot;
            }
            return slot.SlotPosIdx;
        }

        protected virtual void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
        }

        protected virtual AIActionResult OnStartUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            return InProgress;
        }

        protected virtual AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            return InProgress;
        }
        protected virtual AIActionResult OnUseSlotFail(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            return Failed(FailMsgSlotUnavailable);
        }

        protected virtual AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            return Failed(FailMsgSlotUnavailable);
        }
    }
}
