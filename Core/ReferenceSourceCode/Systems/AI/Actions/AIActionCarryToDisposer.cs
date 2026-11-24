using Game.Components;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Input;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class AIActionCarryToDisposer : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionCarryToDisposer());
        }
        public const string ActId = "CarryToDisposer";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionCarryToDisposer() : base(ActId, () => T.VerbDispose) {
            Preconditions = AIState.With(AIVarsH.IsDisposed, false)
                .And(AIVarsH.IsEquipmentStripped, true);
            Outcomes = AIState.With(AIVarsH.IsDisposed, true);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // disposable comp
            agent.BB.Object1 = null;
            agent.BB.ClearBlacklist();
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Object1 = null;
            agent.BB.UnreserveSlot(false, false);
            agent.Being.Graphics.DropIfCarrying();
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.BB.Object1 is not DisposableComp disp) {
                disp = goal.Target as DisposableComp;
                agent.BB.Object1 = disp;
            }
            if (Pos.SqrDistance(agent.PosIdx, goal.Target.Entity.PosIdx) > 2) {
                return Failed(T.AdRejTargetUnavailable);
            }

            if (agent.BB.ReservedSlot == null) {
                if (TryReserveDisposer(S, agent, disp, out var res)) {
                    return res;
                }
            } else {
                var slot = agent.BB.ReservedSlot;
                // we should be near the slot
                if (Pos.SqrDistance(agent.PosIdx, slot.SlotPosIdx) > 2) {
                    agent.BB.AddToBlacklist(slot.Parent.EntityId);
                    agent.BB.ClearReservedSlot();
                    agent.Motion.StoreNavFailureAt(slot.SlotPosIdx);
                    return InProgress;
                }

                agent.Being.Graphics.DropIfCarrying();
                if (slot.Parent is DisposerComp disposer) {
                    if (!disposer.LoadDisposable(disp, agent)) {
                        // try another disposer if available
                        if (S.Sys.Slots.HasFreeFor(disp.SlotDesignation)) {
                            agent.BB.ClearReservedSlot();
                            agent.BB.ClearMoveTarget();
                            if (TryReserveDisposer(S, agent, disp, out var res)) {
                                return res;
                            }
                        }
                        return Failed($"{T.AdRejProcessorQueueFull}: {disposer.Tile.NameLink}");
                    }
                } else {
                    D.Err("Disposer is not a DisposerComp! {0}", slot.Parent.Tile);
                    return Failed($"{T.RequiredDeviceIsNotAvailable}: {slot.Parent.Tile.Definition.CodexLink}");
                }
                goal.WithWorld(AIVarsH.IsDisposed, true);
                return Success;
            }

            var dev = S.Sys.Slots.FirstFor(disp.SlotDesignation, out int count);
            if (dev != null) {
                var devLink = count == 1 ? dev.Tile.NameLink : dev.Tile.Definition.CodexLink;
                if (S.Sys.Slots.HasFreeFor(disp.SlotDesignation)) {
                    // unreachable
                    return Failed($"{T.WorkerCouldNotReachTheDevice}: {agent.BeingData.Mind.LinkToShortName} {GlyphMap.ArrowRight} {devLink}");
                } else {
                    return Failed($"{T.AdRejProcessorQueueFull}: {devLink}");
                }
            }
            return Failed(disp.DisposerUnavailableMessage());
        }

        private bool TryReserveDisposer(GameState S, AIAgentComp agent, DisposableComp disp, out AIActionResult result) {
            // 1. is there something that we can carry the being to?
            var slot = S.Sys.Slots.FindForDesignation<ISlots>(
                agent.BeingData, disp.SlotDesignation,
                agent.BB.Blacklist, null, out var device);
            result = InProgress;

            if (slot != null) {
                if (agent.Being.Graphics.Carrying != disp.Entity) {
                    if (!agent.Being.Graphics.Carry(disp.Entity)) {
                        // someone had a non-interruptable task, it's ok
                        result = Failed(T.AdRejInterrupted);
                        return true;
                    }
                }
                if (!TryGoToOnce(agent, slot.SlotPosIdx, false)) {
                    agent.BB.AddToBlacklist(slot.Parent.EntityId);
                    agent.BB.ClearMoveTarget();
                    return true;
                }
                agent.BB.SetReservedSlot(slot);
                return true;
            }
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target.Entity == agent.Being) {
                // cannot move yourself
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 2f;
        }
    }
}
