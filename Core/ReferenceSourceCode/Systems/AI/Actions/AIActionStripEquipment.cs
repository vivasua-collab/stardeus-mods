using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Unity.Mathematics;

namespace Game.Systems.AI {
    public sealed class AIActionStripEquipment : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStripEquipment());
        }
        public const string ActId = "StripEquipment";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionStripEquipment() : base(ActId, () => T.StripEquipment) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true);
            Outcomes = AIState.With(AIVarsH.IsEquipmentStripped, true);
            WithRequiredAbilities(AbilityIdH.UseItem, AbilityIdH.DumbLabor);
            MarkAsWork();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {

            if (agent.BB.Bool1NotSaved) {
                // inventory is empty
                goal.WithWorld(AIVarsH.IsEquipmentStripped, true);
                return Success;
            }

            if (agent.BB.Object1 is not InventoryComp inv) {
                inv = ResolveInventory(goal);
                // if we're here, there is no inventory, we are likely checking
                // for disposal
                if (inv == null) {
                    goal.WithWorld(AIVarsH.IsEquipmentStripped, true);
                    return Success;
                }
                if (!Pos.AreAdjacent(inv.Being.PosIdx, agent.PosIdx)) {
                    return FailedNavToTarget(agent, inv.Being.PosIdx);
                }
                var b = inv.Being;
                if (b.IsAlive && !b.Motion.IsImmobilized) {
                    b.Motion.Immobilize(ImmobilizeReasonIdH.Operation, true);
                }
                agent.BB.Object1 = inv;
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer10Minutes) {
                // Gear off a corpse or a downed being stays where it fell until the
                // player allows it, so haulers don't carry it off on their own.
                var stripped = inv.Being;
                bool forbid = Tunables.AutoForbidStrippedEquipment
                    && (!stripped.IsAlive || stripped.Data.Body.IsIncapacitated);
                foreach (var slot in inv.Inventory.Equipment) {
                    if (slot.Value.Item != null) {
                        var obj = slot.Value.RemoveItem();
                        if (forbid && obj?.StorableObj != null) {
                            obj.StorableObj.IsForbidden = true;
                        }
                    }
                }
                goal.WithWorld(AIVarsH.IsEquipmentStripped, true);
                return Success;
            }

            // progress bar and wait
            var opProgress = agent.TicksInCurrentAction / (float) Consts.TicksPer10Minutes;
            agent.ShowInfoBar(Consts.ColorGreenBar, opProgress, T.StripEquipment);
            return InProgress;
        }

        private static InventoryComp ResolveInventory(AIGoal goal) {
            InventoryComp inv = null;
            if (goal.Target is InventoryComp inv1) {
                inv = inv1;
            } else {
                // this is likely part of carry to disposer, this object
                if (goal.Target.Entity is Being b) {
                    if (b.Data.Inventory != null) {
                        if (b.TryGetComponent(out inv1)) {
                            inv = inv1;
                        } else {
                            D.Err("No InventoryComp on Being! {0}",
                                goal.Target);
                        }
                    }
                }
            }

            return inv;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // InventoryComp
            agent.BB.Object1 = null;
            // Assert that inventory is empty
            agent.BB.Bool1NotSaved = true;
            var inv = ResolveInventory(goal);
            if (inv == null || inv.Inventory?.Equipment.Count == 0) {
                return;
            }
            foreach (var slot in inv.Inventory.Equipment) {
                if (slot.Value.Item != null) {
                    // inventory is not empty
                    agent.BB.Bool1NotSaved = false;
                    return;
                }
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Object1 is InventoryComp inv) {
                var b = inv.Being;
                if (b.IsAlive && b.Motion.IsImmobilized) {
                    b.Motion.Immobilize(ImmobilizeReasonIdH.Operation, false);
                }
            }
            agent.BB.Object1 = null;
            agent.BB.Bool1NotSaved = false;
            agent.HideInfoBar();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
        }
    }
}
